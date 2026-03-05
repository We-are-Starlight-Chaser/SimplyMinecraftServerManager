
namespace SimplyMinecraftServerManager.Models
{
    public record class ServerProperty(string Key, string Value)
    {
        public string Key { get; set; } = Key;
        public string Value { get; set; } = Value;
    }
}
