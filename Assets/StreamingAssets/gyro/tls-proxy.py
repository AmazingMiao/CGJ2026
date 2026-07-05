#!/usr/bin/env python3
"""TLS 终止反向代理(随 Unity 构建打包的版本)。

给 Unity 内的明文 HTTP/WS 服务加一层真正的 TLS:Unity 编辑器内的 SslStream 走 UnityTLS,
无法与 iOS Safari 完成握手(实测 UNITYTLS_INTERNAL_ERROR)。因此让 GyroServer 只跑明文,
由本脚本用系统 OpenSSL 在 :8443 终止 TLS,再把裸字节透传给 127.0.0.1:8080。WebSocket 升级
与 gyro.html 的 HTTP GET 走同一条 TCP 连接,原始字节透传天然同时覆盖两者,无需解析协议。

与 tools/ 下旧版的区别:启动时会自动"按当前 IP 校验/生成证书",不用再手动跑 make-cert.sh。
证书目录、绑定 IP、端口都可由环境变量传入(GyroProxyLauncher 会传),也可用命令行覆盖。

环境变量:
    GYRO_IP        证书要绑定的 IP(默认自动探测本机局域网 IP)
    GYRO_CERT_DIR  证书读写目录(默认脚本同级 ../certs;打包后建议传可写目录)

用法:
    python3 tls-proxy.py                 # :8443 -> 127.0.0.1:8080,IP/证书目录自动
    python3 tls-proxy.py 8443 8080       # 自定义 监听端口 后端端口
"""

import os
import signal
import socket
import ssl
import subprocess
import sys
import threading
import time

BACKEND_HOST = "127.0.0.1"
CERT_DAYS = 30


def free_port(port: int) -> None:
    """启动前清理占用监听端口的残留进程(通常是上一次没退干净的代理)。"""
    my_pid = os.getpid()
    pids: list[str] = []
    try:
        if sys.platform.startswith("win"):
            out = subprocess.run(
                ["netstat", "-ano", "-p", "tcp"], capture_output=True, text=True
            ).stdout
            for line in out.splitlines():
                parts = line.split()
                if (len(parts) >= 5 and parts[0].upper() == "TCP"
                        and parts[1].endswith(f":{port}") and parts[3].upper() == "LISTENING"):
                    pids.append(parts[4])
        else:
            out = subprocess.run(
                ["lsof", "-ti", f"tcp:{port}", "-sTCP:LISTEN"], capture_output=True, text=True
            ).stdout
            pids = out.split()
    except OSError:
        return

    killed = False
    for pid in pids:
        try:
            pid_i = int(pid)
        except ValueError:
            continue
        if pid_i == my_pid:
            continue
        try:
            if sys.platform.startswith("win"):
                subprocess.run(["taskkill", "/PID", str(pid_i), "/F"], capture_output=True)
            else:
                os.kill(pid_i, signal.SIGKILL)
            print(f"[proxy] 已清理占用端口 {port} 的残留进程 pid={pid_i}")
            killed = True
        except OSError:
            pass

    if killed:
        time.sleep(0.3)  # 等内核释放端口


def detect_lan_ip() -> str:
    """探测本机主用局域网 IP(UDP connect 不实际发包,只用于查本地出口地址)。"""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        return sock.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        sock.close()


def cert_matches(cert_path: str, ip: str) -> bool:
    """证书存在、未过期(至少还剩 1 天)且 SAN 含目标 IP 才算可复用。"""
    if not os.path.exists(cert_path):
        return False
    try:
        subprocess.run(
            ["openssl", "x509", "-in", cert_path, "-checkend", "86400", "-noout"],
            check=True, capture_output=True,
        )
        text = subprocess.run(
            ["openssl", "x509", "-in", cert_path, "-noout", "-text"],
            check=True, capture_output=True, text=True,
        ).stdout
        return f"IP Address:{ip}" in text
    except (OSError, subprocess.CalledProcessError):
        return False


def generate_cert(cert_path: str, key_path: str, ip: str) -> bool:
    """用 OpenSSL 3.x 生成满足 iOS 要求的自签证书(SAN + serverAuth EKU)。"""
    os.makedirs(os.path.dirname(cert_path), exist_ok=True)
    try:
        subprocess.run(
            [
                "openssl", "req", "-x509", "-newkey", "rsa:2048", "-sha256",
                "-days", str(CERT_DAYS), "-nodes",
                "-keyout", key_path, "-out", cert_path,
                "-subj", f"/CN={ip}",
                "-addext", f"subjectAltName=IP:{ip},DNS:localhost,IP:127.0.0.1",
                "-addext", "extendedKeyUsage=serverAuth",
                "-addext", "keyUsage=digitalSignature,keyEncipherment",
                "-addext", "basicConstraints=critical,CA:TRUE",
            ],
            check=True, capture_output=True, text=True,
        )
        print(f"[proxy] 已生成证书(CN={ip}):{cert_path}")
        return True
    except OSError:
        print("[proxy] 找不到 openssl,无法自动生成证书。请先手动跑 tools/make-cert.sh。")
        return False
    except subprocess.CalledProcessError as exc:
        print(f"[proxy] 生成证书失败:{exc.stderr}")
        return False


def ensure_cert(cert_dir: str, ip: str):
    cert_path = os.path.join(cert_dir, "gyro.crt")
    key_path = os.path.join(cert_dir, "gyro.key")
    if cert_matches(cert_path, ip):
        print(f"[proxy] 复用已有证书(匹配 {ip}):{cert_path}")
        return cert_path, key_path
    generate_cert(cert_path, key_path, ip)
    return cert_path, key_path


def pipe(src: socket.socket, dst: socket.socket) -> None:
    try:
        while True:
            data = src.recv(65536)
            if not data:
                break
            dst.sendall(data)
    except OSError:
        pass
    finally:
        try:
            dst.shutdown(socket.SHUT_WR)
        except OSError:
            pass


def send_https_redirect(raw: socket.socket, listen_port: int) -> None:
    """明文 http 连到 TLS 端口时回 308,浏览器会自动改用 https 重连。"""
    try:
        raw.settimeout(2.0)
        data = raw.recv(4096)
        raw.settimeout(None)
        lines = data.decode("latin1", "replace").split("\r\n")
        path = "/gyro.html"
        parts = lines[0].split(" ")
        if len(parts) >= 2 and parts[1]:
            path = parts[1]
        host = None
        for ln in lines[1:]:
            if ln.lower().startswith("host:"):
                host = ln.split(":", 1)[1].strip()
                break
        if not host:
            host = f"127.0.0.1:{listen_port}"
        resp = (
            "HTTP/1.1 308 Permanent Redirect\r\n"
            f"Location: https://{host}{path}\r\n"
            "Content-Length: 0\r\nConnection: close\r\n\r\n"
        )
        raw.sendall(resp.encode("ascii"))
    except OSError:
        pass
    finally:
        try:
            raw.close()
        except OSError:
            pass


def handle(tls_conn: socket.socket, backend_port: int) -> None:
    try:
        backend = socket.create_connection((BACKEND_HOST, backend_port), timeout=5)
    except OSError as exc:
        print(f"[proxy] 后端 {BACKEND_HOST}:{backend_port} 连接失败:{exc} —— Unity 是否已在跑?")
        try:
            tls_conn.close()
        except OSError:
            pass
        return

    up = threading.Thread(target=pipe, args=(tls_conn, backend), daemon=True)
    down = threading.Thread(target=pipe, args=(backend, tls_conn), daemon=True)
    up.start()
    down.start()
    up.join()
    down.join()
    for s in (tls_conn, backend):
        try:
            s.close()
        except OSError:
            pass


def main() -> int:
    listen_port = int(sys.argv[1]) if len(sys.argv) > 1 else 8443
    backend_port = int(sys.argv[2]) if len(sys.argv) > 2 else 8080

    script_dir = os.path.dirname(os.path.abspath(__file__))
    cert_dir = os.environ.get("GYRO_CERT_DIR", os.path.join(script_dir, "..", "certs"))
    cert_dir = os.path.abspath(cert_dir)
    ip = os.environ.get("GYRO_IP") or detect_lan_ip()

    cert_path, key_path = ensure_cert(cert_dir, ip)
    if not (os.path.exists(cert_path) and os.path.exists(key_path)):
        print("[proxy] 无可用证书,退出。")
        return 1

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(certfile=cert_path, keyfile=key_path)

    # 启动前清理端口:上一次没退干净的代理会一直占着 8443,这里先干掉它。
    free_port(listen_port)

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        listener.bind(("0.0.0.0", listen_port))
    except OSError as exc:
        print(f"[proxy] 绑定端口 {listen_port} 失败:{exc}。端口仍被占用,请手动结束占用进程后重试。")
        listener.close()
        return 1
    listener.listen(64)

    print(f"[proxy] TLS 监听 0.0.0.0:{listen_port} -> {BACKEND_HOST}:{backend_port}")
    print(f"[proxy] iPhone 打开 https://{ip}:{listen_port}/gyro.html")

    try:
        while True:
            raw, _ = listener.accept()
            # 偷看首字节:0x16=TLS handshake;否则是明文 http(地址栏把 https 补成了 http)。
            is_tls = True
            try:
                raw.settimeout(2.0)
                head = raw.recv(1, socket.MSG_PEEK)
                raw.settimeout(None)
                is_tls = bool(head) and head[0] == 0x16
            except OSError:
                pass

            if not is_tls:
                threading.Thread(target=send_https_redirect, args=(raw, listen_port), daemon=True).start()
                continue

            try:
                tls_conn = ctx.wrap_socket(raw, server_side=True)
            except (ssl.SSLError, OSError) as exc:
                print(f"[proxy] TLS 握手失败:{exc} —— 手机是否已'完全信任'该证书?")
                try:
                    raw.close()
                except OSError:
                    pass
                continue

            threading.Thread(target=handle, args=(tls_conn, backend_port), daemon=True).start()
    except KeyboardInterrupt:
        print("\n[proxy] 已停止。")
    finally:
        listener.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
