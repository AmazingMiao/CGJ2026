using UnityEngine;
using UnityEngine.Tilemaps;

namespace CGJ2026.Gameplay
{
    public enum ButtonWallChannel
    {
        Button1,
        Button2,
        Button3,
        Button4
    }

    [CreateAssetMenu(fileName = "ButtonWallTile", menuName = "CGJ2026/Tiles/Button Wall Tile")]
    public sealed class ButtonWallTile : Tile
    {
        [SerializeField, InspectorName("按钮通道")] private ButtonWallChannel channel = ButtonWallChannel.Button1;

        public ButtonWallChannel Channel => channel;
    }
}
