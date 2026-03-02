namespace SimplyMinecraftServerManager.Services
{
    /// <summary>
    /// 导航参数传递服务
    /// </summary>
    public class NavigationParameterService
    {
        private string? _pendingInstanceId;

        /// <summary>
        /// 设置待导航的实例 ID
        /// </summary>
        public void SetInstanceId(string instanceId)
        {
            _pendingInstanceId = instanceId;
        }

        /// <summary>
        /// 获取并清除待导航的实例 ID
        /// </summary>
        public string? GetAndClearInstanceId()
        {
            var id = _pendingInstanceId;
            _pendingInstanceId = null;
            return id;
        }
    }
}
