#!/usr/bin/env python3
"""TLS 终止反向代理:给 Unity 内的明文 HTTP/WS 服务加一层真正的 TLS。

背景:Unity 编辑器内的 SslStream 用的是 UnityTLS,无法与 iOS Safari 完成 TLS 握手
(实测报 UNITYTLS_INTERNAL_ERROR)。因此让 GyroServer 只跑明文,由本脚本(Python 标准库
ssl,底层是系统 OpenSSL,握手兼容 Safari)在 :8443 终止 TLS,再把裸字节透传给
127.0.0.1:8080。因为 WebSocket 升级也走同一条 TCP 连接,原始字节透传天然同时覆盖
gyro.html 的 HTTP GET 和 /ws 的 wss 帧,无需解析协议。

用法:
    python3 tools/tls-proxy.py            # 默认 :8443 -> 127.0.0.1:8080
    python3 tools/tls-proxy.py 8443 8080  # 自定义 监听端口 后端端口

证书:复用 certs/gyro.crt + certs/gyro.key(由 tools/make-cert.sh 生成)。
先在 Unity 里按 Play 启动明文服务端,再运行本脚本。
iPhone 打开 https://<PC_IP>:8443/gyro.html,遇自签名警告点“继续访问”。
"""

import os
import socket
import ssl
import sys
import threading

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CERT_PATH = os.path.join(SCRIPT_DIR, "..", "certs", "gyro.crt")
KEY_PATH = os.path.join(SCRIPT_DIR, "..", "certs", "gyro.key")

BACKEND_HOST = "127.0.0.1"

# 直接由代理提供的静态页面目录(Unity 的 websocket-sharp 在本机版本上不触发 OnGet,
# 无法可靠地发静态文件;故 HTTP GET 由代理负责,只有 /ws 透传给 Unity)。
STATIC_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "Assets", "StreamingAssets", "gyro"))

CONTENT_TYPES = {
    ".html": "text/html; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".ico": "image/x-icon",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".json": "application/json; charset=utf-8",
}

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


def send_https_redirect(raw: socket.socket, addr, listen_port: int) -> None:
    """客户端误用明文 http 连到 TLS 端口时,回一个 308 跳转到 https 同址,让浏览器自动改用 HTTPS。"""
    try:
        raw.settimeout(2.0)
        data = raw.recv(4096)
        raw.settimeout(None)
        text = data.decode("latin1", "replace")
        lines = text.split("\r\n")
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
        location = f"https://{host}{path}"
        resp = (
            "HTTP/1.1 308 Permanent Redirect\r\n"
            f"Location: {location}\r\n"
            "Content-Length: 0\r\n"
            "Connection: close\r\n\r\n"
        )
        raw.sendall(resp.encode("ascii"))
    except OSError:
        pass
    finally:
        try:
            raw.close()
        except OSError:
            pass


def read_http_headers(sock: socket.socket, timeout: float = 5.0):
    """读到 \r\n\r\n 为止,返回 (header_bytes, leftover_body_bytes)。"""
    sock.settimeout(timeout)
    buf = b""
    try:
        while b"\r\n\r\n" not in buf:
            chunk = sock.recv(4096)
            if not chunk:
                break
            buf += chunk
            if len(buf) > 65536:
                break
    except OSError:
        pass
    finally:
        try:
            sock.settimeout(None)
        except OSError:
            pass
    idx = buf.find(b"\r\n\r\n")
    if idx == -1:
        return buf, b""
    return buf[: idx + 4], buf[idx + 4:]


def serve_static(tls_conn: socket.socket, addr, path: str) -> None:
    p = path.split("?", 1)[0]
    if p in ("", "/"):
        p = "/gyro.html"
    rel = p.lstrip("/")
    full = os.path.abspath(os.path.join(STATIC_ROOT, rel))
    if not full.startswith(STATIC_ROOT) or not os.path.isfile(full):
        body = b"<html><body><h1>404 Not Found</h1></body></html>"
        resp = (
            b"HTTP/1.1 404 Not Found\r\nContent-Type: text/html\r\n"
            b"Content-Length: " + str(len(body)).encode() + b"\r\nConnection: close\r\n\r\n" + body
        )
        try:
            tls_conn.sendall(resp)
        except OSError:
            pass
        return
    with open(full, "rb") as fh:
        body = fh.read()
    ext = os.path.splitext(full)[1].lower()
    ctype = CONTENT_TYPES.get(ext, "application/octet-stream")
    header = (
        "HTTP/1.1 200 OK\r\nContent-Type: %s\r\nContent-Length: %d\r\nConnection: close\r\n\r\n"
        % (ctype, len(body))
    ).encode("ascii")
    try:
        tls_conn.sendall(header + body)
    except OSError:
        pass


def tunnel_to_backend(tls_conn: socket.socket, addr, backend_port: int, prefix: bytes) -> None:
    """把已读的请求头 + 后续字节透传到 Unity 的明文 WS 服务。"""
    try:
        backend = socket.create_connection((BACKEND_HOST, backend_port), timeout=5)
        # create_connection 会把这 5s 超时留在 socket 上;隧道里 recv 必须无限阻塞,
        # 否则 backend->client 方向空闲 5s 就 socket.timeout,导致连接被误杀(每 5s 断线)。
        backend.settimeout(None)
    except OSError as exc:
        print(f"[proxy] 后端 {BACKEND_HOST}:{backend_port} 连接失败({addr}): {exc} —— Unity 是否已按 Play?")
        try:
            tls_conn.close()
        except OSError:
            pass
        return

    if prefix:
        try:
            backend.sendall(prefix)
        except OSError:
            pass
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


def handle(tls_conn: socket.socket, addr, backend_port: int) -> None:
    try:
        header_bytes, leftover = read_http_headers(tls_conn)
        if not header_bytes:
            try:
                tls_conn.close()
            except OSError:
                pass
            return

        text = header_bytes.decode("latin1", "replace")
        lines = text.split("\r\n")
        parts = lines[0].split(" ")
        path = parts[1] if len(parts) > 1 else "/"
        headers_lower = text.lower()
        is_ws = ("upgrade: websocket" in headers_lower) or ("upgrade:websocket" in headers_lower) or path.split("?", 1)[0] == "/ws"

        if is_ws:
            # websocket-sharp 的 WebSocketServer 会校验 Host 头是否匹配它的监听端点。
            # 浏览器发来的 Host 是代理地址(172.20.10.2:8443),需改写成后端地址,否则握手 400。
            new_host = f"{BACKEND_HOST}:{backend_port}"
            hdr_lines = header_bytes.decode("latin1").split("\r\n")
            for i, ln in enumerate(hdr_lines):
                if ln.lower().startswith("host:"):
                    hdr_lines[i] = "Host: " + new_host
                    break
            rewritten = "\r\n".join(hdr_lines).encode("latin1")
            tunnel_to_backend(tls_conn, addr, backend_port, rewritten + leftover)
        else:
            serve_static(tls_conn, addr, path)
            try:
                tls_conn.close()
            except OSError:
                pass
    except OSError:
        try:
            tls_conn.close()
        except OSError:
            pass


def main() -> int:
    listen_port = int(sys.argv[1]) if len(sys.argv) > 1 else 8443
    backend_port = int(sys.argv[2]) if len(sys.argv) > 2 else 8080

    if not os.path.exists(CERT_PATH) or not os.path.exists(KEY_PATH):
        print(f"[proxy] 找不到证书:\n  {CERT_PATH}\n  {KEY_PATH}\n先运行:./tools/make-cert.sh <PC局域网IP>")
        return 1

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(certfile=CERT_PATH, keyfile=KEY_PATH)

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind(("0.0.0.0", listen_port))
    listener.listen(64)

    print(f"[proxy] TLS 监听 0.0.0.0:{listen_port} -> {BACKEND_HOST}:{backend_port}")
    print(f"[proxy] iPhone 打开 https://<PC热点IP>:{listen_port}/gyro.html")

    try:
        while True:
            raw, addr = listener.accept()
            # 偷看首字节:0x16=TLS handshake;否则是明文 http(常见于地址栏把 https 自动补全成 http)。
            is_tls = True
            try:
                raw.settimeout(2.0)
                head = raw.recv(48, socket.MSG_PEEK)
                raw.settimeout(None)
                first_byte = head[0] if head else -1
                is_tls = first_byte == 0x16
            except OSError:
                pass

            if not is_tls:
                # 明文 http 连到了 TLS 端口:回 308 跳转到 https,浏览器会自动改用 HTTPS 重连。
                print(f"[proxy] 收到明文 HTTP({addr}),回 308 跳转到 https。请在手机上用 https:// 访问。")
                threading.Thread(target=send_https_redirect, args=(raw, addr, listen_port), daemon=True).start()
                continue

            try:
                tls_conn = ctx.wrap_socket(raw, server_side=True)
            except (ssl.SSLError, OSError) as exc:
                print(f"[proxy] TLS 握手失败({addr}): {exc}")
                try:
                    raw.close()
                except OSError:
                    pass
                continue
            threading.Thread(target=handle, args=(tls_conn, addr, backend_port), daemon=True).start()
    except KeyboardInterrupt:
        print("\n[proxy] 已停止。")
    finally:
        listener.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
