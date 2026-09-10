namespace PlateCounterflowHeatExchanger
{
    // Every player-visible string, as a LocString tree. README, "Localization".
    //
    // The root is named STRINGS on purpose. LocString.CreateLocStringKeys(typeof(STRINGS), null)
    // walks this tree and registers each field under "STRINGS.<Nested>.<Path>.<FIELD>", which is
    // exactly the dotted key the game itself uses: building text is looked up at
    // STRINGS.BUILDINGS.PREFABS.<ID_UPPER>.*, and the StatusItem constructor at
    // STRINGS.BUILDING.STATUSITEMS.<id_upper>.*. So the class names below must match those
    // paths letter for letter (upper case, and PLATECOUNTERFLOWHEATEXCHANGER is the building
    // id upper-cased). Inside this namespace the name shadows the game's own STRINGS class;
    // code that wants the vanilla one writes global::STRINGS.
    //
    // Localization.RegisterForTranslation(typeof(STRINGS)) registers the same tree a second
    // time under "PlateCounterflowHeatExchanger.STRINGS.*", which is the key form translation
    // (.po) files address, and lists our assembly with the game's translation loader.
    public static class STRINGS
    {
        public static class BUILDINGS
        {
            public static class PREFABS
            {
                public static class PLATECOUNTERFLOWHEATEXCHANGER
                {
                    // Vanilla wraps every building name in a codex link (<link="ID">name</link>);
                    // link text draws in the link colour, so a plain string stood out in lists
                    // such as the research-progress tooltip. The link id is the codex entry id,
                    // which for buildings is the prefab id upper-cased.
                    public static LocString NAME = global::STRINGS.UI.FormatAsLink("Counterflow Heat Exchanger", "PLATECOUNTERFLOWHEATEXCHANGER");
                    // DESC is what the automatic Database (codex) entry shows, so it carries
                    // the short version of the model. EFFECT is the build-menu one-liner.
                    public static LocString DESC =
                        "A passive plate heat exchanger; it draws no power. Two liquid streams run past each other in " +
                        "opposite directions through a stack of thin metal plates, and heat crosses the plates from the " +
                        "hotter stream to the colder one. Metals with higher thermal conductivity move more heat.\n\n" +
                        "Some liquids leave deposits that foul the plates and cut heat transfer. Flow also scours " +
                        "deposits away, so fouling levels off instead of climbing without limit, and levels off lower " +
                        "at high flow. A Duplicant is sent to clean the plates at 50% fouling.\n\n" +
                        "The shell insulation sets how much heat leaks to the room. Fluids hotter than the plate metal's " +
                        "melting point melt the building.";
                    public static LocString EFFECT =
                        "Transfers heat between two fluid streams. Fouls over time and needs periodic cleaning.";
                }
            }
        }

        public static class BUILDING
        {
            public static class STATUSITEMS
            {
                // {Placeholders} are filled by the callbacks in PCHXStatusItems.
                //
                // Tooltip lines are kept under about 80 characters with explicit breaks. The
                // side-panel status tooltip sizes itself to its longest line instead of
                // wrapping, and a paragraph-length line ran off both screen edges (check f,
                // 2026-09-07). The long-form explanation of fouling lives in DESC (codex).
                public static class PCHX_FOULING
                {
                    public static LocString NAME = "Fouling: {Fouling}";
                    public static LocString TOOLTIP =
                        "Deposits on the plates add thermal resistance.\n" +
                        "Heat transfer is down {Fouling} from clean.\n\n" +
                        "Flow deposits and also scours, so fouling levels off,\n" +
                        "and levels off lower at high flow.\n" +
                        "Hot plates speed scaling and coking;\n" +
                        "above 72 °C they stop biological growth.\n\n" +
                        "A Duplicant is sent to clean at {Threshold}.\n\n" +
                        "{Deposits}";
                }

                public static class PCHX_FLOW
                {
                    public static LocString NAME = "Flow: A {FlowA}, B {FlowB}";
                    public static LocString TOOLTIP =
                        "{StreamA}: {FlowA}\n" +
                        "{StreamB}: {FlowB}\n\n" +
                        "Effectiveness: {Effectiveness}\n" +
                        "The share of the largest heat transfer possible\n" +
                        "between these two streams. Throttling a stream raises it;\n" +
                        "fouling lowers it.\n\n" +
                        "Rates are averaged over 3 seconds.";
                }

                public static class PCHX_CLEANINGORDERED
                {
                    public static LocString NAME = "Cleaning ordered";
                    public static LocString TOOLTIP =
                        "A Duplicant will open the plate pack and remove the deposits.\n" +
                        "Both streams stop while the plates are open.";
                }

                public static class PCHX_NEEDSCLEANING
                {
                    public static LocString NAME = "Needs cleaning";
                    public static LocString TOOLTIP =
                        "Exchanger plates are fouled.\n" +
                        "Efficiency keeps falling until the plates are cleaned.";
                }

                public static class PCHX_PHASECHANGERISK
                {
                    public static LocString NAME = "Output near phase change";
                    public static LocString TOOLTIP =
                        "{Detail}\n\n" +
                        "A fluid that freezes or boils inside a pipe breaks the pipe.\n" +
                        "Throttle a stream, or bring the other inlet closer in temperature.";
                }

                // One item per port, so the hover card (which shows names only) says which.
                public static class PCHX_NOPIPE_A_IN
                {
                    public static LocString NAME = "No pipe: Stream A input (bottom-left)";
                    public static LocString TOOLTIP = "Connect a liquid pipe to this port.\nA stream with a missing port does not flow.";
                }

                public static class PCHX_NOPIPE_A_OUT
                {
                    public static LocString NAME = "No pipe: Stream A output (bottom-right)";
                    public static LocString TOOLTIP = "Connect a liquid pipe to this port.\nA stream with a missing port does not flow.";
                }

                public static class PCHX_NOPIPE_B_IN
                {
                    public static LocString NAME = "No pipe: Stream B input (top-right)";
                    public static LocString TOOLTIP = "Connect a liquid pipe to this port.\nA stream with a missing port does not flow.";
                }

                public static class PCHX_NOPIPE_B_OUT
                {
                    public static LocString NAME = "No pipe: Stream B output (top-left)";
                    public static LocString TOOLTIP = "Connect a liquid pipe to this port.\nA stream with a missing port does not flow.";
                }
            }
        }

        public static class DUPLICANTS
        {
            public static class CHORES
            {
                // Errand name, the Duplicant's status while working, and its tooltip. No
                // placeholders, so nothing depends on Chore.ResolveString's conventions.
                public static class PCHX_CLEANPLATES
                {
                    public static LocString NAME = "Clean Plates";
                    public static LocString STATUS = "Cleaning heat exchanger plates";
                    public static LocString TOOLTIP = "This Duplicant is removing deposits from a heat exchanger's plates";
                }
            }
        }

        public static class UI
        {
            public static class PCHX
            {
                // Building menu buttons.
                public static LocString CLEAN_BUTTON = "Clean Plates";
                public static LocString CLEAN_BUTTON_TOOLTIP =
                    "Order a Duplicant to open the plate pack and remove deposits.\nBoth streams stop during cleaning.";
                public static LocString CANCEL_CLEAN_BUTTON = "Cancel Cleaning";
                public static LocString CANCEL_CLEAN_BUTTON_TOOLTIP = "Withdraw the cleaning order.";

                // Stream names for tooltips (unrotated layout; see the config).
                public static LocString STREAM_A = "Stream A (bottom)";
                public static LocString STREAM_B = "Stream B (top)";

                // Deposit list: "{0}: {1}" per stream, and the word for an empty ledger.
                public static LocString DEPOSIT_LINE = "{0}: {1}";
                public static LocString NO_DEPOSITS = "clean";

                // Effectiveness readout when the last tick exchanged nothing: a stream idle,
                // or the plate pack open for cleaning.
                public static LocString NO_EXCHANGE = "none (no flow)";

                // {0} stream, {1} outlet temperature, {2} fluid name, {3} transition temperature.
                public static LocString PHASE_FREEZE = "{0} leaves at {1};\n{2} freezes at {3}.";
                public static LocString PHASE_BOIL = "{0} leaves at {1};\n{2} boils at {3}.";
            }
        }
    }
}
