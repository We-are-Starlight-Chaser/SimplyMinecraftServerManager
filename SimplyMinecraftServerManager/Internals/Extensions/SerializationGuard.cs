using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// Guards against serialization attacks by blocking unsafe serialization methods.
/// Extensions should use the project's serialization services instead.
/// </summary>
internal sealed class SerializationGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ExtensionLogger? _logger;
    private bool _disposed;
    
    // Dangerous serialization types that should be blocked
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
    
    // Dangerous serialization methods
    private static readonly HashSet<string> DangerousSerializationMethods = new()
    {
        "Deserialize",
        "DeserializeObject",
        "DeserializeFromStream",
        "Populate",
        "ReadObject",
    };
    
    // File extensions that should be blocked for serialization
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
    /// Checks if a serialization call is allowed.
    /// Returns true if the call should be blocked.
    /// </summary>
    public bool IsSerializationCallBlocked(Type? serializationType, string? methodName = null)
    {
        if (_disposed)
            return false;
        
        if (serializationType == null)
            return false;
        
        var typeName = serializationType.FullName ?? serializationType.Name;
        
        // Check if type is dangerous
        if (IsDangerousSerializationType(typeName))
        {
            _logger?.Warn($"Blocked serialization call to dangerous type {typeName} in extension {_extensionId}");
            return true;
        }
        
        // Check if method is dangerous
        if (methodName != null && IsDangerousSerializationMethod(methodName))
        {
            _logger?.Warn($"Blocked dangerous serialization method {methodName} in extension {_extensionId}");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if a file path is safe for serialization operations.
    /// Returns true if the path should be blocked.
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
            
            // Check if file extension is dangerous
            if (DangerousFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                _logger?.Warn($"Blocked serialization to file with dangerous extension {extension} in extension {_extensionId}");
                return true;
            }
            
            // Check if file path contains suspicious patterns
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
    /// Checks if a type is in the dangerous list.
    /// </summary>
    private bool IsDangerousSerializationType(string typeName)
    {
        return DangerousSerializationTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Checks if a method name is in the dangerous list.
    /// </summary>
    private bool IsDangerousSerializationMethod(string methodName)
    {
        return DangerousSerializationMethods.Contains(methodName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Validates that serialized data is safe.
    /// </summary>
    public bool IsSerializedDataSafe(byte[] data)
    {
        if (_disposed || data == null || data.Length == 0)
            return true;
        
        // Check for BinaryFormatter signature (0x00 0x01 0x00 0x00 0x00)
        if (data.Length >= 5 &&
            data[0] == 0x00 && data[1] == 0x01 && data[2] == 0x00 && data[3] == 0x00 && data[4] == 0x00)
        {
            _logger?.Warn($"Blocked BinaryFormatter serialized data in extension {_extensionId}");
            return false;
        }
        
        // Check for .NET serialization header patterns
        if (data.Length >= 4)
        {
            // Check for SOAP header
            if (data[0] == '<' && data[1] == '?' && data[2] == 'x' && data[3] == 'm')
            {
                _logger?.Warn($"Blocked SOAP serialized data in extension {_extensionId}");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Gets the list of dangerous serialization types.
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
