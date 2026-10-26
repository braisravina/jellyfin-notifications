namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// How many people have set preferences, as counts only: never a name or an address.
/// </summary>
/// <param name="Total">People with a preferences record.</param>
/// <param name="WantDigests">Of those, how many have digests turned on.</param>
/// <param name="WillReceive">Of those, how many also have a verified address, so would be sent a digest.</param>
/// <param name="WaitingForVerifiedAddress">Digests on, but no verified address yet, so nothing is sent to them.</param>
public sealed record SubscriberCountsDto(int Total, int WantDigests, int WillReceive, int WaitingForVerifiedAddress);
