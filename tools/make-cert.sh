#!/usr/bin/env bash
# 生成自签名 TLS 证书,供 PC 端 wss 服务端(websocket-sharp)使用。
# iPhone Safari 首次访问 https://<PC_IP>:<PORT> 时会弹证书警告,手动“继续访问”后,
# 同源的 wss:// 会复用该信任,不再报混合内容/证书错误。
#
# 用法:
#   ./tools/make-cert.sh <PC局域网IP>
# 例:
#   ./tools/make-cert.sh 192.168.43.1
#
# 产物:certs/gyro.pfx(供 GyroServer 加载,密码见下方 PFX_PASSWORD)

set -euo pipefail

PC_IP="${1:-}"
PFX_PASSWORD="gyro"
OUT_DIR="$(cd "$(dirname "$0")/.." && pwd)/certs"

if [[ -z "$PC_IP" ]]; then
  echo "用法: $0 <PC局域网IP>   例如: $0 192.168.43.1"
  echo "提示:开热点后用 'ipconfig getifaddr en0' 或系统设置查看本机 IP。"
  exit 1
fi

mkdir -p "$OUT_DIR"

# iOS 13+ 对 TLS 服务端证书有硬性结构要求,不满足会“连接丢失”且不给“继续访问”选项:
#   - 必须含 subjectAltName(SAN)
#   - 必须含 extendedKeyUsage = serverAuth
#   - keyUsage 需包含 digitalSignature / keyEncipherment
#   - RSA >= 2048、SHA-256+、有效期 <= 825 天(此处 30 天)
# 缺 serverAuth EKU 时 Safari 会直接判为“无法建立/已丢失连接”,而不是弹自签名警告。
openssl req -x509 -newkey rsa:2048 -sha256 -days 30 -nodes \
  -keyout "$OUT_DIR/gyro.key" \
  -out "$OUT_DIR/gyro.crt" \
  -subj "/CN=$PC_IP" \
  -addext "subjectAltName=IP:$PC_IP,DNS:localhost,IP:127.0.0.1" \
  -addext "extendedKeyUsage=serverAuth" \
  -addext "keyUsage=digitalSignature,keyEncipherment" \
  -addext "basicConstraints=critical,CA:TRUE"

# 打包成 PKCS#12(.pfx)。
# ⚠️ Unity 的 Mono 运行时用的是老式 PKCS#12 解析器,只认 HMAC-SHA1 + PBE-SHA1-3DES;
# OpenSSL 3.x 默认会用 HMAC-SHA256 + AES-256-CBC,导致 Unity 报 "unsupported HMAC"。
# 因此必须用 -legacy 并显式指定老算法,生成 Mono 能读的 pfx。
openssl pkcs12 -export -legacy \
  -inkey "$OUT_DIR/gyro.key" \
  -in "$OUT_DIR/gyro.crt" \
  -out "$OUT_DIR/gyro.pfx" \
  -certpbe PBE-SHA1-3DES \
  -keypbe PBE-SHA1-3DES \
  -macalg sha1 \
  -passout "pass:$PFX_PASSWORD"

echo ""
echo "✅ 证书已生成:$OUT_DIR/gyro.pfx (密码: $PFX_PASSWORD)"
echo "   在 GyroServer 组件里确认 IP=$PC_IP,然后运行场景。"
echo "   iPhone 打开:https://$PC_IP:8443/gyro.html"
