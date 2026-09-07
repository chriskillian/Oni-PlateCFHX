namespace PlateCounterflowHeatExchanger
{
    // The building's status items. A StatusItem is a template with one instance per KIND of
    // message, shared by every building that shows it. Per-building content comes from the
    // callbacks, which receive whatever object was passed to KSelectable.AddStatusItem as
    // "data" (e.g. the HeatExchangerCore) and fill the placeholders in the string.
    //
    // Text lives in STRINGS.BUILDING.STATUSITEMS.<ID_UPPER>.NAME / .TOOLTIP (see Mod.cs);
    // the constructor looks those keys up from the id and the "BUILDING" prefix. Created
    // in the Db.Initialize postfix, after the game's own status items exist.
    //
    // All of this is inferred from the vanilla pattern based on the constructor's parameter
    // order and the callback signature (string, object) -> string, not from verifying against
    // decompiled game code. Check here first if this file fails to compile.
    public static class PCHXStatusItems
    {
        public static StatusItem Fouling;
        public static StatusItem CleaningOrdered;

        public static void Create()
        {
            // Named arguments would break on any parameter-name difference, even
            // when the order is right, so this is positional on purpose:
            // (id, prefix, icon, icon_type, notification_type, allow_multiples, render_overlay)

            Fouling = new StatusItem(
                "PCHX_Fouling", "BUILDING", "",
                StatusItem.IconType.Info, NotificationType.Neutral,
                false, OverlayModes.None.ID);
            Fouling.resolveStringCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str.Replace("{Fouling}", GameUtil.GetFormattedPercent(core.FoulingFraction() * 100f));
            };
            Fouling.resolveTooltipCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str
                    .Replace("{Fouling}", GameUtil.GetFormattedPercent(core.FoulingFraction() * 100f))
                    .Replace("{Threshold}", GameUtil.GetFormattedPercent(FoulingCleanWorkable.AutoCleanThreshold * 100f))
                    .Replace("{Deposits}", core.DescribeDeposits());
            };

            CleaningOrdered = new StatusItem(
                "PCHX_CleaningOrdered", "BUILDING", "",
                StatusItem.IconType.Info, NotificationType.Neutral,
                false, OverlayModes.None.ID);
        }
    }
}
