using System.Collections.Generic;
using TfsSystemInfoExtractor.Core.Reporting;

namespace TfsSystemInfoExtractor.Tests.Fakes
{
    internal static class ReportViewBuilder
    {
        public static ReportView Sample(params string[] extraFieldKeys)
        {
            var view = new ReportView
            {
                Title = "TFS System Info",
                GeneratedAt = "2026-09-08 14:30",
                Columns =
                {
                    new ReportColumn { Key = "id", Label = "ID", Kind = ReportColumnKind.Id },
                    new ReportColumn { Key = "type", Label = "Type", Kind = ReportColumnKind.Type },
                    new ReportColumn { Key = "title", Label = "Title", Kind = ReportColumnKind.Text },
                    new ReportColumn { Key = "state", Label = "State", Kind = ReportColumnKind.Text },
                    new ReportColumn { Key = "systemInfo", Label = "System Info", Kind = ReportColumnKind.Multiline }
                }
            };

            foreach (var key in extraFieldKeys)
            {
                view.Columns.Add(new ReportColumn { Key = "x:" + key, Label = key, Kind = ReportColumnKind.Text });
            }

            view.Rows.Add(Row(0, true, false, "#46269", "Feature", "Onboarding portal", "Active", "Browser: Chrome\nOS: Win11", extraFieldKeys));
            view.Rows.Add(Row(1, false, false, "#46270", "User Story", "כותרת בעברית — mixed", "Resolved", "שרת: WEB03", extraFieldKeys));
            view.Rows.Add(Row(1, false, true, "#99999", "Bug", "(failed to load)", "", "Work Item 99999 does not exist (404).", extraFieldKeys));
            view.Html = "<div class=\"tv-wrap\"><table class=\"tv\"><tbody class=\"tv-grp\"><tr class=\"tv-root\"><td>#46269</td></tr></tbody></table></div>";
            return view;
        }

        private static ReportRow Row(int depth, bool groupStart, bool error,
            string id, string type, string title, string state, string systemInfo, string[] extraKeys)
        {
            var cells = new Dictionary<string, string>
            {
                ["id"] = id, ["type"] = type, ["title"] = title, ["state"] = state, ["systemInfo"] = systemInfo
            };
            foreach (var key in extraKeys)
            {
                cells["x:" + key] = key + "-value";
            }

            return new ReportRow { Depth = depth, GroupStart = groupStart, Error = error, Cells = cells };
        }
    }
}
