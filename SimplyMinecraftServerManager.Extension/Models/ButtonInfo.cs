using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Extension.Models
{
    public record ButtonInfo(
        string Content,
        SymbolRegular Icon,
        ControlAppearance Appearance,
        Action OnClick
    )
    {
        public ButtonInfo() : this("", SymbolRegular.Empty, ControlAppearance.Secondary, () => { }) { }
    }
}