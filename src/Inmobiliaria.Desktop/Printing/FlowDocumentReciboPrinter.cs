using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Desktop.Printing;

/// <summary>
/// Draws the print model on paper through the Windows print dialog (design section 9.2). It asks for A4
/// landscape; from the printable area the operator's choice gives, <see cref="SheetArrangement"/> decides
/// two-up or one half per page. One FlowDocument holds every sheet; content wraps inside the half, never
/// scaled down. Printing never touches the database. Checked by hand (M1 to M3), not by tests.
/// </summary>
public sealed class FlowDocumentReciboPrinter : IReciboPrinter
{
    private const double BodySize = 12;   // 9 pt in device-independent pixels (D11).
    private const double FooterSize = 10.67; // 8 pt.
    private const double Margin = 24;

    private readonly string _logoPath;

    public FlowDocumentReciboPrinter(string logoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logoPath);
        _logoPath = logoPath;
    }

    public Task<PrintOutcome> PrintAsync(IReadOnlyList<ReciboSheet> sheets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
        {
            return Task.FromResult(PrintOutcome.Cancelled);
        }

        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        if (dialog.ShowDialog() != true)
        {
            return Task.FromResult(PrintOutcome.Cancelled);
        }

        var width = dialog.PrintableAreaWidth;
        var height = dialog.PrintableAreaHeight;
        var layout = SheetArrangement.For(
            SheetArrangement.Millimetres(width), SheetArrangement.Millimetres(height), HalfPageCount(sheets[0].Original, width / 2, height));

        var document = NewDocument(width, height);
        if (layout == SheetLayout.TwoUp)
        {
            document.ColumnWidth = (width - 3 * Margin) / 2;
            document.ColumnGap = Margin;
            document.IsColumnWidthFlexible = false;
        }
        else
        {
            document.ColumnWidth = width;
        }

        for (var i = 0; i < sheets.Count; i++)
        {
            var original = Half(sheets[i].Original);
            original.BreakPageBefore = i > 0;
            document.Blocks.Add(original);

            var duplicado = Half(sheets[i].Duplicado);
            if (layout == SheetLayout.TwoUp)
            {
                duplicado.BreakColumnBefore = true;
            }
            else
            {
                duplicado.BreakPageBefore = true;
            }

            document.Blocks.Add(duplicado);
        }

        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"Recibo {sheets[0].NumberText}");
        return Task.FromResult(PrintOutcome.Printed);
    }

    /// <summary>How many pages one half takes at the given width: more than one means it cannot go two-up.</summary>
    private int HalfPageCount(ReciboHalf half, double width, double height)
    {
        var probe = NewDocument(width, height);
        probe.ColumnWidth = width;
        probe.Blocks.Add(Half(half));
        var paginator = ((IDocumentPaginatorSource)probe).DocumentPaginator;
        paginator.ComputePageCount();
        return paginator.PageCount;
    }

    private static FlowDocument NewDocument(double width, double height) => new()
    {
        PageWidth = width,
        PageHeight = height,
        PagePadding = new Thickness(Margin),
        FontFamily = new FontFamily("Arial"),
        FontSize = BodySize,
    };

    private Section Half(ReciboHalf half)
    {
        var c = half.Content;
        var section = new Section();

        if (c.Annulment is { } annulment)
        {
            section.Blocks.Add(new Paragraph(new Bold(new Run($"{annulment.Word}  {annulment.DateText}")))
            {
                FontSize = 20,
                Foreground = Brushes.DarkRed,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
            section.Blocks.Add(Text("Motivo: " + annulment.Reason, FooterSize, TextAlignment.Center, Brushes.DarkRed));
        }

        section.Blocks.Add(Header(c, half.Caption));
        section.Blocks.Add(new Paragraph(new Bold(new Run(c.ServiceBand)))
        {
            Background = Brushes.Black,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(2),
        });
        section.Blocks.Add(Text($"Locatario/a: {c.Tenant}"));
        section.Blocks.Add(Text($"Propietario: {c.Lessors}"));
        section.Blocks.Add(Text($"Unidad: {c.Units}"));
        section.Blocks.Add(Text(c.CobroLine, FooterSize));
        section.Blocks.Add(Rows(c));
        section.Blocks.Add(Text(c.AmountInWordsText));
        section.Blocks.Add(Text(c.PayerLine));
        section.Blocks.Add(Text("I.V.A.:  " + string.Join("   ", c.IvaBoxes.Select(b => "☐ " + b)), FooterSize));
        section.Blocks.Add(Text(c.RentalActivityLegend, FooterSize));
        section.Blocks.Add(Text("Firma Locador: ______________________", FooterSize));
        section.Blocks.Add(Text($"{c.BrokerName} · {c.BrokerTitle} · {c.LicenceText}       Firma: ______________", FooterSize));

        return section;
    }

    private Table Header(Inmobiliaria.Presentation.Printing.ReciboHalfContent c, string caption)
    {
        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(70) });
        table.Columns.Add(new TableColumn());
        table.Columns.Add(new TableColumn());

        var logo = new Image { Source = Logo(), Width = 64, Height = 64 };
        var agency = new Paragraph { FontSize = FooterSize };
        agency.Inlines.Add(new Bold(new Run(c.Agency.Name)));
        agency.Inlines.Add(new LineBreak());
        agency.Inlines.Add(new Run(c.Agency.Address));
        agency.Inlines.Add(new LineBreak());
        agency.Inlines.Add(new Run("Tel/fax: " + c.Agency.Phone));
        agency.Inlines.Add(new LineBreak());
        agency.Inlines.Add(new Run("Email: " + c.Agency.Email));
        agency.Inlines.Add(new LineBreak());
        agency.Inlines.Add(new Bold(new Run(c.Agency.TaxStatus)));

        var recibo = new Paragraph { TextAlignment = TextAlignment.Right };
        recibo.Inlines.Add(new Bold(new Run(caption)));
        recibo.Inlines.Add(new LineBreak());
        recibo.Inlines.Add(new Bold(new Run($"{c.Title} {c.NumberText}")) { FontSize = 14 });
        recibo.Inlines.Add(new LineBreak());
        recibo.Inlines.Add(new Run("Fecha: " + c.DateText));
        recibo.Inlines.Add(new LineBreak());
        recibo.Inlines.Add(new Run(c.RecipientType));
        recibo.Inlines.Add(new LineBreak());
        recibo.Inlines.Add(new Run("[X] " + c.NonFiscalLegend) { FontSize = FooterSize });

        var group = new TableRowGroup();
        var row = new TableRow();
        row.Cells.Add(new TableCell(new BlockUIContainer(logo)));
        row.Cells.Add(new TableCell(agency));
        row.Cells.Add(new TableCell(recibo));
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        return table;
    }

    private static Table Rows(Inmobiliaria.Presentation.Printing.ReciboHalfContent c)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        table.Columns.Add(new TableColumn());
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });

        var group = new TableRowGroup();
        foreach (var r in c.Rows)
        {
            group.Rows.Add(RowOf(r.Label, r.AmountText, bold: false));
        }

        group.Rows.Add(RowOf("Importe Neto", c.ImporteNetoText, bold: true));
        table.RowGroups.Add(group);
        return table;
    }

    private static TableRow RowOf(string label, string amount, bool bold)
    {
        var row = new TableRow();
        Inline Inline(string text) => bold ? new Bold(new Run(text)) : new Run(text);
        row.Cells.Add(new TableCell(new Paragraph(Inline(label)) { Margin = new Thickness(2, 0, 2, 0) }));
        row.Cells.Add(new TableCell(new Paragraph(Inline(amount)) { TextAlignment = TextAlignment.Right, Margin = new Thickness(2, 0, 2, 0) }));
        return row;
    }

    private static Paragraph Text(string text, double size = BodySize, TextAlignment alignment = TextAlignment.Left, Brush? brush = null) =>
        new(new Run(text)) { FontSize = size, TextAlignment = alignment, Foreground = brush ?? Brushes.Black, Margin = new Thickness(0, 2, 0, 2) };

    private BitmapImage Logo()
    {
        if (!File.Exists(_logoPath))
        {
            throw new FileNotFoundException("The agency logo file is missing.", _logoPath);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(_logoPath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
