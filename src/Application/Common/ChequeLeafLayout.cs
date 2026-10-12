namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Fill-in positions for a Meezan Premium leaf, 7.5 × 3.5 in (190.5 × 88.9 mm).
/// Measured on the printed cheque: date boxes sit under Cheque No., the Pay rule is
/// below the branch line, and the PKR box is only as tall as the first Rupees rule.
/// Values are millimetres from the top-left.
/// </summary>
public static class ChequeLeafLayout
{
    public const float WidthMm = 190.5f;
    public const float HeightMm = 88.9f;

    // Eight DDMMYYYY boxes under the cheque number. Pitch is the border-to-border step.
    // The last digit must stay inside the eighth box, clear of the date-end edge.
    public const float DateLeftMm = 135.2f;
    public const float DateTopMm = 17.7f;
    public const float DateHeightMm = 8.3f;
    public const float DateBoxPitchMm = 6.41f;

    // Pay rule. The name starts after "Pay" and stops before "or bearer".
    // Measured on the 12 Oct print: "Cash" started off the left edge and above the rule.
    public const float PayeeLineMm = 41.2f;
    public const float PayeeLeftMm = 30f;
    public const float PayeeWidthMm = 92f;

    // First Rupees rule is beside the PKR box. The second rule is below that box.
    // Measured on the 12 Oct print: the words started at the left edge, above the Rupees rule.
    public const float WordsLine1Mm = 47.6f;
    public const float WordsLine2Mm = 55.2f;
    public const float WordsLeftMm = 32f;
    public const float WordsWidthMm = 88f;

    // PKR box, right of the first Rupees rule. Not the tall standard figures rectangle.
    // Measured on the 12 Oct print: the figures sat a little above the middle of the box.
    public const float FiguresLeftMm = 134.3f;
    public const float FiguresTopMm = 38.7f;
    public const float FiguresWidthMm = 52.6f;
    public const float FiguresHeightMm = 7.5f;

    // "PKR" is already printed in the left of the box.
    public const float FiguresTextInsetMm = 11f;
    public const float FiguresTextRightPadMm = 2.5f;

    // Above the Pay rule, clear of the premium banner and the date boxes.
    public const float CrossingLeftMm = 58f;
    public const float CrossingTopMm = 26f;
    public const float CrossingWidthMm = 42f;
    public const float CrossingHeightMm = 5.4f;

    public const float MaxShiftMm = 60f;

    // Printers list A4. The cheque is fed date-end first, so on that A4 page the
    // leaf is turned: 88.9 mm across and 190.5 mm along the feed, date end at the top.
    public const float A4WidthMm = 210f;
    public const float A4HeightMm = 297f;
    public const float FeedSpanMm = HeightMm;

    // Centre of the A4 page. A cheque in the centre feeder sits on this band.
    // Left-guide printing uses 0 instead.
    public static float CentreFeedOriginX => (A4WidthMm - FeedSpanMm) / 2f;
}
