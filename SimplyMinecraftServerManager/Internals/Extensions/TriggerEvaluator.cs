// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 触发器条件求值器。
/// 根据触发器参数和触发上下文判断是否满足触发条件。
/// </summary>
internal sealed class TriggerEvaluator
{
    /// <summary>
    /// 判断触发器是否满足条件可以执行。
    /// </summary>
    public static bool Evaluate(ExtensionTrigger trigger, TriggerContext context)
    {
        // 1. 检查触发器类型是否匹配
        if (!trigger.Type.HasFlag(context.TriggerType))
        {
            return false;
        }

        // 2. 检查实例过滤
        if (trigger.Parameters.TryGetValue("instanceId", out string? requiredInstanceId))
        {
            if (string.IsNullOrEmpty(context.InstanceId) ||
                !string.Equals(context.InstanceId, requiredInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 3. 检查自定义条件表达式
        if (!string.IsNullOrWhiteSpace(trigger.Condition))
        {
            if (!EvaluateCondition(trigger.Condition, context))
            {
                return false;
            }
        }

        // 4. 检查附加参数匹配
        if (!EvaluateParameters(trigger.Parameters, context))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 评估自定义条件表达式。
    /// 支持简单表达式：
    ///   - "time BETWEEN 08:00 AND 22:00"
    ///   - "data[playerCount] > 50"
    ///   - "instanceId == 'xxx'"
    /// </summary>
    private static bool EvaluateCondition(string condition, TriggerContext context)
    {
        string trimmed = condition.Trim();

        // time BETWEEN start AND end
        if (trimmed.StartsWith("time BETWEEN", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateTimeBetween(trimmed, context);
        }

        // data[key] op value
        if (trimmed.StartsWith("data[", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateDataExpression(trimmed, context);
        }

        // instanceId == 'value'
        if (trimmed.StartsWith("instanceId", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateInstanceIdExpression(trimmed, context);
        }

        // 未知表达式默认通过
        return true;
    }

    private static bool EvaluateTimeBetween(string expression, TriggerContext context)
    {
        // 格式: time BETWEEN HH:mm AND HH:mm
        try
        {
            int betweenIdx = expression.IndexOf("BETWEEN", StringComparison.OrdinalIgnoreCase);
            int andIdx = expression.LastIndexOf("AND", StringComparison.OrdinalIgnoreCase);

            if (betweenIdx < 0 || andIdx < 0 || andIdx <= betweenIdx) return false;

            string startStr = expression[(betweenIdx + 7)..andIdx].Trim().Trim('\'', '"');
            string endStr = expression[(andIdx + 3)..].Trim().Trim('\'', '"');

            if (!TimeOnly.TryParse(startStr, out TimeOnly start) ||
                !TimeOnly.TryParse(endStr, out TimeOnly end))
            {
                return false;
            }

            TimeOnly now = TimeOnly.FromDateTime(context.Timestamp.ToLocalTime());

            if (start <= end)
            {
                return now >= start && now <= end;
            }
            else
            {
                // 跨午夜，如 22:00 BETWEEN 08:00
                return now >= start || now <= end;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool EvaluateDataExpression(string expression, TriggerContext context)
    {
        // 格式: data[key] > value / data[key] == value
        try
        {
            int bracketStart = expression.IndexOf('[');
            int bracketEnd = expression.IndexOf(']');
            if (bracketStart < 0 || bracketEnd < 0) return false;

            string key = expression[(bracketStart + 1)..bracketEnd];
            string opAndValue = expression[(bracketEnd + 1)..].Trim();

            if (!context.Data.TryGetValue(key, out string? dataValue))
            {
                return false;
            }

            string op = opAndValue switch
            {
                _ when opAndValue.StartsWith("==") => "==",
                _ when opAndValue.StartsWith("!=") => "!=",
                _ when opAndValue.StartsWith(">=") => ">=",
                _ when opAndValue.StartsWith("<=") => "<=",
                _ when opAndValue.StartsWith('>') => ">",
                _ when opAndValue.StartsWith('<') => "<",
                _ => "=="
            };

            string valueStr = opAndValue[op.Length..].Trim().Trim('\'', '"');

            if (double.TryParse(dataValue, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double dataNum) &&
                double.TryParse(valueStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double valueNum))
            {
                return op switch
                {
                    "==" => dataNum == valueNum,
                    "!=" => dataNum != valueNum,
                    ">=" => dataNum >= valueNum,
                    "<=" => dataNum <= valueNum,
                    ">" => dataNum > valueNum,
                    "<" => dataNum < valueNum,
                    _ => false
                };
            }

            return op switch
            {
                "==" => string.Equals(dataValue, valueStr, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(dataValue, valueStr, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool EvaluateInstanceIdExpression(string expression, TriggerContext context)
    {
        // 格式: instanceId == 'value' / instanceId != 'value'
        try
        {
            string op = expression.Contains("==") ? "==" : expression.Contains("!=") ? "!=" : "==";
            int opIdx = expression.IndexOf(op, StringComparison.Ordinal);
            string valueStr = expression[(opIdx + op.Length)..].Trim().Trim('\'', '"');

            return op switch
            {
                "==" => string.Equals(context.InstanceId, valueStr, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(context.InstanceId, valueStr, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 评估触发器的附加参数是否匹配当前上下文。
    /// </summary>
    private static bool EvaluateParameters(Dictionary<string, string> parameters, TriggerContext context)
    {
        // 对于 PlayerJoined/PlayerLeft，检查 playerName 参数
        if (context.TriggerType is TriggerType.PlayerJoined or TriggerType.PlayerLeft)
        {
            if (parameters.TryGetValue("playerName", out string? requiredPlayer))
            {
                if (context.Data.TryGetValue("playerName", out string? actualPlayer))
                {
                    if (!string.Equals(requiredPlayer, actualPlayer, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
        }

        // 对于 LogPattern，检查 pattern 参数（由宿主预匹配）
        // 对于 Timer，由 TriggerManager 验证时间间隔
        // 对于 MemoryThreshold，由 TriggerManager 验证阈值

        return true;
    }
}
