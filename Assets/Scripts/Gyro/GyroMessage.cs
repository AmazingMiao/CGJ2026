#nullable enable

using System;

namespace CGJ2026.Gyro
{
    /// 手机 → PC:一帧姿态 + 加速度。字段名与 gyro.html 里 JSON.stringify 的键一一对应(供 JsonUtility 解析)。
    /// 角度来自 DeviceOrientationEvent(度);加速度来自 DeviceMotionEvent(m/s²)。
    [Serializable]
    public class TiltMessage
    {
        /// 绕 X 轴(前后倾),范围约 -180~180。
        public float Beta;

        /// 绕 Y 轴(左右倾),范围约 -90~90。
        public float Gamma;

        /// 绕 Z 轴(朝向),范围约 0~360;阶段 1 暂不用,先收着。
        public float Alpha;

        /// 含重力加速度(accelerationIncludingGravity),m/s²。静止时其模约等于 9.8。
        public float AccelX;
        public float AccelY;
        public float AccelZ;

        /// 去重力线性加速度(acceleration),m/s²。部分设备/浏览器不提供时为 0。
        public float LinAccelX;
        public float LinAccelY;
        public float LinAccelZ;
    }
}
