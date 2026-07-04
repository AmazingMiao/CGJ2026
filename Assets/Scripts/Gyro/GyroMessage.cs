#nullable enable

using System;

namespace CGJ2026.Gyro
{
    /// 手机 → PC:一帧陀螺仪倾角。字段名与 gyro.html 里 JSON.stringify 的键一一对应(供 JsonUtility 解析)。
    /// 角度单位为度,来自浏览器 DeviceOrientationEvent。
    [Serializable]
    public class TiltMessage
    {
        /// 绕 X 轴(前后倾),范围约 -180~180。
        public float Beta;

        /// 绕 Y 轴(左右倾),范围约 -90~90。
        public float Gamma;

        /// 绕 Z 轴(朝向),范围约 0~360;阶段 1 暂不用,先收着。
        public float Alpha;
    }
}
