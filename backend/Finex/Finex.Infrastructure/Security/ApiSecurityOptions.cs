using System.ComponentModel.DataAnnotations;

namespace Finex.Infrastructure.Security
{
    public sealed class ApiSecurityOptions
    {
        public const string SectionName = "ApiSecurity";

        [Required]
        [MinLength(32)]
        public string CurrentSecret { get; set; } = string.Empty;

        public string? PreviousSecret { get; set; }

        [Range(10, 600)]
        public int AllowedClockSkewSeconds { get; set; } = 180;

        [Range(1024, 104_857_600)]
        public int MaxPayloadBytes { get; set; } = 5 * 1024 * 1024;

        [Range(1024, 52_428_800)]
        public int MaxResponseBytes { get; set; } = 5 * 1024 * 1024;
    }
}