using System.Runtime.Serialization;

namespace AutomatasFinal.Domain.Entities;

public enum AnalysisStatus
{
    [EnumMember(Value = "PENDING")]
    PENDING,

    [EnumMember(Value = "PROCESSING")]
    PROCESSING,

    [EnumMember(Value = "COMPLETED")]
    COMPLETED,

    [EnumMember(Value = "FAILED")]
    FAILED
}