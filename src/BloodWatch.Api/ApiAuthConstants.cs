namespace BloodWatch.Api;

internal static class ApiAuthConstants
{
    public const string BearerSecuritySchemeId = "Bearer";
    public const string CopilotApiKeySecuritySchemeId = "CopilotAdminApiKey";
    public const string SubscriptionWritePolicyName = "SubscriptionWritePolicy";
    public const string AuthTokenRateLimitPolicyName = "AuthTokenPolicy";
    public const string CopilotRateLimitPolicyName = "copilot-internal";
    public const string CopilotApiKeyHeaderName = "X-Admin-Api-Key";
    public const string RoleClaimType = "bw:role";
    public const string AdminRoleValue = "admin";
}
