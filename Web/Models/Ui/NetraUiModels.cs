using Microsoft.AspNetCore.Html;
using NETRA.Web.Models.Navigation;

namespace NETRA.Web.Models.Ui;

/// <summary>Visual tone shared by badges, stats and alerts.</summary>
public enum NetraTone
{
    Neutral,
    Primary,
    Info,
    Success,
    Warning,
    Danger
}

/// <summary>Display-only formatting for enum-backed values shown in the UI.</summary>
public static class NetraDisplay
{
    /// <summary>Splits PascalCase enum names into title-case words ("UnderInvestigation" → "Under Investigation").</summary>
    public static string Words(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return string.Concat(value.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + ch : ch.ToString()));
    }
}

public sealed class StatusBadgeModel
{
    public string Text { get; set; } = string.Empty;

    public NetraTone Tone { get; set; } = NetraTone.Neutral;

    /// <summary>Render a leading status dot, matching the NETRA legend style.</summary>
    public bool ShowDot { get; set; } = true;
}

public sealed class StatCardModel
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? Meta { get; set; }

    public NetraTone Tone { get; set; } = NetraTone.Neutral;

    public NetraIcon Icon { get; set; } = NetraIcon.Default;

    public bool ShowIcon { get; set; }
}

public sealed class PageHeaderModel
{
    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    /// <summary>Optional pre-rendered meta line (status legend, key facts) under the subtitle.</summary>
    public HtmlString? Meta { get; set; }

    /// <summary>Optional pre-rendered markup placed on the right of the header.</summary>
    public HtmlString? Actions { get; set; }
}

public sealed class BreadcrumbItemModel
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Null or empty for the current (last) item.</summary>
    public string? Url { get; set; }
}

public sealed class BreadcrumbsModel
{
    public IReadOnlyList<BreadcrumbItemModel> Items { get; set; } =
        Array.Empty<BreadcrumbItemModel>();
}

public sealed class EmptyStateModel
{
    public string Title { get; set; } = "No records found";

    public string? Text { get; set; }

    public NetraIcon Icon { get; set; } = NetraIcon.Inbox;

    public string? ActionText { get; set; }

    public string? ActionUrl { get; set; }
}

public sealed class PaginationModel
{
    public int Page { get; set; } = 1;

    public int TotalPages { get; set; } = 1;

    public int TotalItems { get; set; }

    public int PageSize { get; set; } = 25;

    public string Action { get; set; } = "Index";

    public string Controller { get; set; } = "Dashboard";

    public IReadOnlyDictionary<string, string?> RouteValues { get; set; } =
        new Dictionary<string, string?>();

    public int Window { get; set; } = 2;

    public int From => TotalItems == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int To => Math.Min(Page * PageSize, TotalItems);

    public IEnumerable<int> PageNumbers
    {
        get
        {
            if (TotalPages <= 1)
            {
                yield break;
            }

            var start = Math.Max(1, Page - Window);
            var end = Math.Min(TotalPages, Page + Window);

            for (var i = start; i <= end; i++)
            {
                yield return i;
            }
        }
    }
}

public sealed class FormSectionModel
{
    public string Title { get; set; } = string.Empty;

    public string? Hint { get; set; }

    /// <summary>
    /// Key in <c>ViewData</c> holding a pre-rendered <see cref="HtmlString"/> for the
    /// section body. Lets a page compose arbitrary fields without duplicating markup.
    /// </summary>
    public string? ContentViewDataKey { get; set; }
}

public enum NetraFilterFieldType
{
    Text,
    Number,
    Date,
    Select,
    Checkbox
}

public sealed class FilterFieldModel
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public NetraFilterFieldType Type { get; set; } = NetraFilterFieldType.Text;

    public string? Value { get; set; }

    public string? Placeholder { get; set; }

    public bool Checked { get; set; }

    public IReadOnlyList<SelectOptionModel> Options { get; set; } = Array.Empty<SelectOptionModel>();
}

public sealed class SelectOptionModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool Selected { get; set; }
}

public sealed class FilterPanelModel
{
    public string Action { get; set; } = "Index";

    public string Controller { get; set; } = "Dashboard";

    public string Method { get; set; } = "get";

    public string SubmitLabel { get; set; } = "Apply";

    public string? ResetUrl { get; set; }

    public IReadOnlyList<FilterFieldModel> Fields { get; set; } = Array.Empty<FilterFieldModel>();

    /// <summary>Optional key in <c>ViewData</c> holding a pre-rendered <see cref="HtmlString"/>.</summary>
    public string? ContentViewDataKey { get; set; }
}

public sealed class TableColumnModel
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Key used to look the cell value up in a row dictionary.</summary>
    public string Key { get; set; } = string.Empty;

    public bool IsNumeric { get; set; }

    /// <summary>Optional sort field name; when set the header renders a sort link.</summary>
    public string? SortField { get; set; }
}

/// <summary>
/// One table row. Values may be plain objects (encoded) or
/// <see cref="HtmlString"/> (rendered as-is) for badges and links.
/// </summary>
public sealed class TableRowModel
{
    public Dictionary<string, object?> Cells { get; set; } = new();
}

public sealed class DataTableModel
{
    public string? Caption { get; set; }

    public IReadOnlyList<TableColumnModel> Columns { get; set; } = Array.Empty<TableColumnModel>();

    public IReadOnlyList<TableRowModel> Rows { get; set; } = Array.Empty<TableRowModel>();

    public EmptyStateModel Empty { get; set; } = new();

    public string SortAction { get; set; } = "Index";

    public string SortController { get; set; } = "Dashboard";

    public string? SortParamName { get; set; } = "sort";

    public string? SortDirection { get; set; }

    /// <summary>Optional key in <c>ViewData</c> holding a pre-rendered footer (e.g. pagination).</summary>
    public string? FooterViewDataKey { get; set; }
}