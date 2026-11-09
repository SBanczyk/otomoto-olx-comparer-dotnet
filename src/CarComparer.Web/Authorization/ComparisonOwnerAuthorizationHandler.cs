using CarComparer.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Security.Claims;

namespace CarComparer.Web.Authorization;

public static class ComparisonOperations
{
    public static readonly OperationAuthorizationRequirement Manage = new() { Name = nameof(Manage) };
}

/// <summary>Only the owner may edit or delete a comparison. Anonymous comparisons (no owner) can be managed by nobody.</summary>
public sealed class ComparisonOwnerAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, Comparison>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, Comparison resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (requirement == ComparisonOperations.Manage && userId is not null && resource.OwnerId == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
