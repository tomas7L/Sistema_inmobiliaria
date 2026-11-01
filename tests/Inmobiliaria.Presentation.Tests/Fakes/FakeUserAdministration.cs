using Inmobiliaria.Domain.Access;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IUserAdministration"/>: the list it answers with is set by the test, each action is
/// recorded and answered by a delegate the test assigned (success when none is), so a test can prove a call was
/// never made.
/// </summary>
internal sealed class FakeUserAdministration : IUserAdministration
{
    public List<UserListing> Users { get; set; } = [];

    public Func<string, string, UserAdminResult>? OnCreate { get; set; }

    public Func<Guid, UserAdminResult>? OnReset { get; set; }

    public Func<Guid, UserAdminResult>? OnDeactivate { get; set; }

    public Func<Guid, UserAdminResult>? OnReactivate { get; set; }

    public List<string> Calls { get; } = [];

    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<UserListing>> ListAsync(CancellationToken ct = default)
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<UserListing>>(Users.ToList());
    }

    public Task<UserAdminResult> CreateEmpleadoAsync(string typedUsername, string displayName, CancellationToken ct = default)
    {
        Calls.Add($"create {typedUsername} {displayName}");
        return Task.FromResult(OnCreate?.Invoke(typedUsername, displayName)
            ?? UserAdminResult.Issued(new ProvisionalCredential(typedUsername, "lago-sol-pan-48017")));
    }

    public Task<UserAdminResult> ResetPasswordAsync(Guid userId, CancellationToken ct = default)
    {
        Calls.Add($"reset {userId}");
        return Task.FromResult(OnReset?.Invoke(userId) ?? UserAdminResult.Issued(new ProvisionalCredential("sofia", "mar-pan-sol-12345")));
    }

    public Task<UserAdminResult> DeactivateAsync(Guid userId, CancellationToken ct = default)
    {
        Calls.Add($"deactivate {userId}");
        return Task.FromResult(OnDeactivate?.Invoke(userId) ?? UserAdminResult.Success);
    }

    public Task<UserAdminResult> ReactivateAsync(Guid userId, CancellationToken ct = default)
    {
        Calls.Add($"reactivate {userId}");
        return Task.FromResult(OnReactivate?.Invoke(userId) ?? UserAdminResult.Issued(new ProvisionalCredential("maria", "rio-luz-pez-67890")));
    }
}

/// <summary>Records every text written and every clear; <see cref="Fails"/> makes a write fail, as a busy clipboard does.</summary>
internal sealed class FakeClipboard : IClipboard
{
    public List<string> Written { get; } = [];

    public List<string> Cleared { get; } = [];

    public bool Fails { get; set; }

    public bool TrySetText(string text)
    {
        if (Fails)
        {
            return false;
        }

        Written.Add(text);
        return true;
    }

    public void ClearIfHolds(string text) => Cleared.Add(text);
}
