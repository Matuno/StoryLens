using FFXIVClientStructs.FFXIV.Client.UI.Info;
using StoryLens.Core;

namespace StoryLens;

internal static class CompanyReader
{
    // Read only, framework thread only; cached list is partial and is never requested or paged by this plugin.
    public static unsafe (string Id, int Total, (WatchedPlayer Player, uint Location)[] Members) Read(WatchedPlayer local)
    {
        var proxy = InfoProxyFreeCompanyMember.Instance();
        if (proxy == null || proxy->FreeCompanyId == 0 || proxy->CharData == null ||
            proxy->InfoProxyCommonList.InfoProxyPageInterface.InfoProxyInterface.EntryCount is 0 or > 200)
            return ("", 0, []);
        var entries = new List<(WatchedPlayer Player, uint Location)>();
        var own = false;
        foreach (var entry in proxy->CharDataSpan)
        {
            var player = new WatchedPlayer(entry.NameString, entry.HomeWorld);
            if (!WatchedPlayer.IsValidName(player.Name) || player.HomeWorldId == 0) continue;
            own |= player.Key == local.Key;
            if (entry.State != 0 && (entry.State & (InfoProxyCommonList.CharacterData.OnlineStatus.Disconnected |
                    InfoProxyCommonList.CharacterData.OnlineStatus.OfflineExd | InfoProxyCommonList.CharacterData.OnlineStatus.NotFound)) == 0 && entry.Location != 0)
                entries.Add((player, entry.Location));
        }
        var company = InfoProxyFreeCompany.Instance();
        var total = company != null && company->Id == proxy->FreeCompanyId && company->TotalMembers <= 512 ? company->TotalMembers : 0;
        return own ? (proxy->FreeCompanyId.ToString(), total, entries.ToArray()) : ("", 0, []);
    }
}
