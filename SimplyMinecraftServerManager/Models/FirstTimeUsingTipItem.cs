using System.Windows.Media.Imaging;

namespace SimplyMinecraftServerManager.Models
{
    public record class FirstTimeUsingTipItem
    {
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public TipItem[] Tips { get; init; } = [];
    }
    public record class TipItem
    {
        public BitmapSource? Gif { get; init; }
        public string Feature { get; init; } = string.Empty;
    }
}
