// SPDX-License-Identifier: GPL-3.0-only
using CMUmcl.Community;

internal static partial class Program
{
    private static void PartialMessageChecks()
    {
        // CF 401 is a project-credential failure, not a CSU session failure.
        {
            string msg = CommunityResourceViewModel.BuildPartialMessage(new List<CommunitySourceException>
            {
                new CommunitySourceException(ResourceSourceId.CurseForge, "x", 401),
            });
            Check(msg.Contains("访问凭据") && !msg.Contains("登录"), "partial: CF 401 uses direct credential wording");
        }

        // CF 503 keeps available Modrinth results.
        {
            string msg = CommunityResourceViewModel.BuildPartialMessage(new List<CommunitySourceException>
            {
                new CommunitySourceException(ResourceSourceId.CurseForge, "x", 503),
            });
            Check(msg.Contains("服务") && !msg.Contains("后端") && msg.Contains("Modrinth"), "partial: CF 503 uses upstream service wording + Modrinth");
        }

        // CF 429 → rate-limit wording.
        {
            string msg = CommunityResourceViewModel.BuildPartialMessage(new List<CommunitySourceException>
            {
                new CommunitySourceException(ResourceSourceId.CurseForge, "x", 429),
            });
            Check(msg.Contains("频繁"), "partial: CF 429 → rate-limit");
        }

        // Modrinth-only failure → generic partial-source wording.
        {
            string msg = CommunityResourceViewModel.BuildPartialMessage(new List<CommunitySourceException>
            {
                new CommunitySourceException(ResourceSourceId.Modrinth, "x", null),
            });
            Check(msg.Contains("部分来源"), "partial: Modrinth-only failure → generic");
        }

        // No failures → empty.
        {
            string msg = CommunityResourceViewModel.BuildPartialMessage(new List<CommunitySourceException>());
            Check(msg.Length == 0, "partial: no failures → empty");
        }
    }
}
