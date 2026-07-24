using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 通过阻止不安全的序列化方法来防止序列化攻击。
/// 扩展应改用项目的序列化服务。
/// </summary>
internal sealed class SerializationGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ExtensionLogger? _logger;
    private bool _disposed;
    
    // 应阻止的危险序列化类型
    private static readonly HashSet<string> DangerousSerializationTypes = new()
    {
        "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter",
        "System.Runtime.Serialization.NetDataContractSerializer",
        "System.Runtime.Serialization.ObjectStateFormatter",
        "System.Web.Script.Serialization.JavaScriptSerializer",
        "System.Runtime.Serialization.DataContractSerializer",
        "System.Runtime.Serialization.DataContractJsonSerializer",
        "System.Xml.Serialization.XmlSerializer",
    };
    
    // 危险的序列化方法
    private static readonly HashSet<string> DangerousSerializationMethods = new()
    {
        "Deserialize",
        "DeserializeObject",
        "DeserializeFromStream",
        "Populate",
        "ReadObject",
    };
    
    // 应阻止序列化的文件扩展名
    private static readonly HashSet<string> DangerousFileExtensions = new()
    {
        ".bin",
        ".dat",
        ".soap",
        ".xml",
        ".json",
    };
    
    public SerializationGuard(string extensionId, ExtensionLogger? logger = null)
    {
        _extensionId = extensionId;
        _logger = logger;
    }
    
    /// <summary>
    /// 检查是否允许进行序列化调用。
    /// 如果调用应被阻止则返回 true。
    /// </summary>
    public bool IsSerializationCallBlocked(Type? serializationType, string? methodName = null)
    {
        if (_disposed)
            return false;
        
        if (serializationType == null)
            return false;
        
        var typeName = serializationType.FullName ?? serializationType.Name;
        
        // 检查类型是否危险
        if (IsDangerousSerializationType(typeName))
        {
            _logger?.Warn($"Blocked serialization call to dangerous type {typeName} in extension {_extensionId}");
            return true;
        }
        
        // 检查方法是否危险
        if (methodName != null && IsDangerousSerializationMethod(methodName))
        {
            _logger?.Warn($"Blocked dangerous serialization method {methodName} in extension {_extensionId}");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 检查文件路径是否对序列化操作安全。
    /// 如果路径应被阻止则返回 true。
    /// </summary>
    public bool IsSerializationPathBlocked(string filePath)
    {
        if (_disposed)
            return false;
        
        if (string.IsNullOrEmpty(filePath))
            return false;
        
        try
        {
            var extension = Path.GetExtension(filePath);
            
            // 检查文件扩展名是否危险
            if (DangerousFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                _logger?.Warn($"Blocked serialization to file with dangerous extension {extension} in extension {_extensionId}");
                return true;
            }
            
            // 检查文件路径是否包含可疑模式
            if (filePath.Contains("...", StringComparison.OrdinalIgnoreCase) ||
                filePath.Contains("..\\", StringComparison.OrdinalIgnoreCase) ||
                filePath.Contains("../", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Warn($"Blocked serialization to suspicious file path in extension {_extensionId}");
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查类型是否在危险列表中。
    /// </summary>
    private bool IsDangerousSerializationType(string typeName)
    {
        return DangerousSerializationTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// 检查方法名是否在危险列表中。
    /// </summary>
    private bool IsDangerousSerializationMethod(string methodName)
    {
        return DangerousSerializationMethods.Contains(methodName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// 验证序列化数据是否安全。
    /// </summary>
    public bool IsSerializedDataSafe(byte[] data)
    {
        if (_disposed || data == null || data.Length == 0)
            return true;
        
        // 检查 BinaryFormatter 签名 (0x00 0x01 0x00 0x00 0x00)
        if (data.Length >= 5 &&
            data[0] == 0x00 && data[1] == 0x01 && data[2] == 0x00 && data[3] == 0x00 && data[4] == 0x00)
        {
            _logger?.Warn($"Blocked BinaryFormatter serialized data in extension {_extensionId}");
            return false;
        }
        
        // 检查 .NET 序列化头模式
        if (data.Length >= 4)
        {
            // 检查 SOAP 头
            if (data[0] == '<' && data[1] == '?' && data[2] == 'x' && data[3] == 'm')
            {
                _logger?.Warn($"Blocked SOAP serialized data in extension {_extensionId}");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 获取危险序列化类型的列表。
    /// </summary>
    public static IReadOnlyList<string> GetDangerousSerializationTypes()
    {
        return DangerousSerializationTypes.ToList().AsReadOnly();
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
