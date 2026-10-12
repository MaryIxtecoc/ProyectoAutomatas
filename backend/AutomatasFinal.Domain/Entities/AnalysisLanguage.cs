using System.Runtime.Serialization;

namespace AutomatasFinal.Domain.Entities;

public enum AnalysisLanguage
{
    [EnumMember(Value = "ES")]
    ES,

    [EnumMember(Value = "EN")]
    EN,

    [EnumMember(Value = "RU")]
    RU,

    [EnumMember(Value = "ZH")]
    ZH,

    [EnumMember(Value = "AR")]
    AR
}