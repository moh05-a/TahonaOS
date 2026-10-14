using Microsoft.Extensions.Options;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Ai;

/// <summary>Reads Ai:PatientData. Anything unrecognised falls back to Minimized, never to Full.</summary>
public sealed class AiDataPolicy(IOptions<AiOptions> options) : IAiDataPolicy
{
    public AiPatientDataMode PatientData { get; } = options.Value.PatientData?.Trim().ToLowerInvariant() switch
    {
        "off" => AiPatientDataMode.Off,
        "full" => AiPatientDataMode.Full,
        _ => AiPatientDataMode.Minimized
    };
}
