namespace PlateCounterflowHeatExchanger
{
    // The building's status items. A StatusItem is a template with one instance per KIND of
    // message, shared by every building that shows it. Per-building content comes from the
    // callbacks, which receive whatever object was passed to KSelectable.AddStatusItem as
    // "data" (e.g. the HeatExchangerCore) and fill the placeholders in the string.
    //
    // Text lives in STRINGS.BUILDING.STATUSITEMS.<ID_UPPER>.NAME / .TOOLTIP (PCHXStrings.cs);
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
        public static StatusItem NeedsCleaning;      // yellow: past threshold, no order pending
        public static StatusItem PortsDisconnected;  // yellow: a port cell has no pipe
        public static StatusItem PhaseChangeRisk;    // yellow: an outlet is near freezing/boiling

        // Add or remove a status item so that its presence matches `on`. `handle` is the
        // caller's saved Guid; RemoveStatusItem hands back Guid.Empty.
        public static void Toggle(KSelectable selectable, StatusItem item, bool on, object data, ref System.Guid handle)
        {
            if (on && handle == System.Guid.Empty)
            {
                handle = selectable.AddStatusItem(item, data);
            }
            else if (!on && handle != System.Guid.Empty)
            {
                handle = selectable.RemoveStatusItem(handle);
            }
        }

        private static string Percent(HeatExchangerCore core) =>
            GameUtil.GetFormattedPercent(core.FoulingPercent());

        private static string Threshold() =>
            GameUtil.GetFormattedPercent(FoulingCleanWorkable.AutoCleanThresholdPercent);

        public static void Create()
        {
            // Named arguments would break on any parameter-name difference, even
            // when the order is right, so this is positional on purpose:
            // (id, prefix, icon, icon_type, notification_type, allow_multiples, render_overlay)

            Fouling = new StatusItem(
                "PCHX_Fouling", "BUILDING", "",
                StatusItem.IconType.Info, NotificationType.Neutral,
                false, OverlayModes.None.ID);
            // The readout and the automatic trigger both use FoulingPercent, an integer, so the
            // formatter has nothing left to round and the two can never disagree.
            Fouling.resolveStringCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str.Replace("{Fouling}", Percent(core));
            };
            Fouling.resolveTooltipCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str
                    .Replace("{Fouling}", Percent(core))
                    .Replace("{Threshold}", Threshold())
                    .Replace("{Deposits}", core.DescribeDeposits());
            };

            CleaningOrdered = new StatusItem(
                "PCHX_CleaningOrdered", "BUILDING", "",
                StatusItem.IconType.Info, NotificationType.Neutral,
                false, OverlayModes.None.ID);

            NeedsCleaning = new StatusItem(
                "PCHX_NeedsCleaning", "BUILDING", "",
                StatusItem.IconType.Exclamation, NotificationType.BadMinor,
                false, OverlayModes.None.ID);
            NeedsCleaning.resolveTooltipCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str.Replace("{Fouling}", Percent(core)).Replace("{Threshold}", Threshold());
            };

            PortsDisconnected = new StatusItem(
                "PCHX_PortsDisconnected", "BUILDING", "",
                StatusItem.IconType.Exclamation, NotificationType.BadMinor,
                false, OverlayModes.LiquidConduits.ID);
            PortsDisconnected.resolveTooltipCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str.Replace("{Ports}", core.MissingPorts);
            };

            PhaseChangeRisk = new StatusItem(
                "PCHX_PhaseChangeRisk", "BUILDING", "",
                StatusItem.IconType.Exclamation, NotificationType.BadMinor,
                false, OverlayModes.None.ID);
            PhaseChangeRisk.resolveTooltipCallback = (str, data) =>
            {
                var core = data as HeatExchangerCore;
                if (core == null) return str;
                return str.Replace("{Detail}", core.PhaseWarning);
            };
        }
    }
}
