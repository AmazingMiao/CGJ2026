#!/usr/bin/env python3
"""UDP 陀螺仪模拟发送器:没手机也能测通 UdpGyroServer 的收包链路。

按 GyroInput 期望的 JSON 格式(字段 Beta / Gamma / Alpha,单位度)持续发包,
默认让 Beta / Gamma 做正弦摆动,方便在 Unity 的 GyroHud 里直接看到数值在动。

用法:
    python3 tools/udp-gyro-sim.py                 # 发到 127.0.0.1:9050,50Hz
    python3 tools/udp-gyro-sim.py 172.20.10.2     # 发到指定 PC IP
    python3 tools/udp-gyro-sim.py 172.20.10.2 9050 50   # IP 端口 频率(Hz)

先在 Unity 里按 Play 启动挂了 UdpGyroServer 的 GyroControlUDP 场景,再运行本脚本。
"""

import json
import math
import socket
import sys
import time


def main() -> int:
    host = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 9050
    rate_hz = float(sys.argv[3]) if len(sys.argv) > 3 else 50.0

    interval = 1.0 / rate_hz
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    addr = (host, port)

    print(f"[udp-gyro-sim] 发送到 {host}:{port},{rate_hz:.0f}Hz。Ctrl+C 停止。")

    start = time.time()
    sent = 0
    try:
        while True:
            t = time.time() - start
            payload = {
                "Beta": round(45.0 * math.sin(t * 1.3), 2),
                "Gamma": round(45.0 * math.sin(t * 0.9 + 1.0), 2),
                "Alpha": round((t * 30.0) % 360.0, 2),
            }
            sock.sendto(json.dumps(payload).encode("utf-8"), addr)
            sent += 1
            if sent % int(max(rate_hz, 1)) == 0:
                print(f"[udp-gyro-sim] 已发 {sent} 包,最新:{payload}")
            time.sleep(interval)
    except KeyboardInterrupt:
        print(f"\n[udp-gyro-sim] 已停止,共发送 {sent} 包。")
    finally:
        sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
