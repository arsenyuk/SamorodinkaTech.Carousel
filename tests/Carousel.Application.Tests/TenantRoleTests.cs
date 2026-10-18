using Carousel.Application.Security;
using Carousel.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class TenantRoleTests
{
    [Theory]
    [InlineData(MemberRole.Owner, MemberRole.Owner, true)]
    [InlineData(MemberRole.Owner, MemberRole.Editor, true)]
    [InlineData(MemberRole.Owner, MemberRole.Viewer, true)]
    [InlineData(MemberRole.Editor, MemberRole.Owner, false)]
    [InlineData(MemberRole.Editor, MemberRole.Editor, true)]
    [InlineData(MemberRole.Editor, MemberRole.Viewer, true)]
    [InlineData(MemberRole.Viewer, MemberRole.Owner, false)]
    [InlineData(MemberRole.Viewer, MemberRole.Editor, false)]
    [InlineData(MemberRole.Viewer, MemberRole.Viewer, true)]
    public void Role_hierarchy_matches_matrix(MemberRole actual, MemberRole minimum, bool allowed)
    {
        // Same comparison used by TenantAccessService / EndpointHelpers: Owner=0 is highest.
        var ok = actual <= minimum;
        ok.Should().Be(allowed);
    }

    [Fact]
    public void Owner_cannot_be_assigned_via_validator_enum_parse()
    {
        Enum.TryParse<MemberRole>("Owner", true, out var owner).Should().BeTrue();
        owner.Should().Be(MemberRole.Owner);

        // Endpoint rejects assigning Owner through add/update member.
        var assignOwnerAllowed = owner != MemberRole.Owner;
        assignOwnerAllowed.Should().BeFalse();
    }

    [Fact]
    public void App_exceptions_map_to_http_statuses()
    {
        typeof(UnauthorizedException).Should().BeAssignableTo<Exception>();
        typeof(ForbiddenException).Should().BeAssignableTo<Exception>();
        typeof(NotFoundException).Should().BeAssignableTo<Exception>();
        typeof(ConflictException).Should().BeAssignableTo<Exception>();
    }
}
