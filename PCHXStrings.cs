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
                    public static LocString NAME = "Plate Counterflow Heat Exchanger";
                    // DESC is what the automatic Database (codex) entry shows, so it carries
                    // the short version of the model. EFFECT is the build-menu one-liner.
                    public static LocString DESC =
                        "A passive plate heat exchanger; it draws no power. Two liquid streams run past each other in " +
                        "opposite directions through a stack of thin metal plates, and heat crosses the plates from the " +
                        "hotter stream to the colder one. Metals with higher thermal conductivity move more heat.\n\n" +
                        "Brine, Salt Water, Polluted Water, Crude Oil, and Petroleum leave deposits that foul the plates " +
                        "and cut heat transfer. Flow also scours deposits away, so fouling levels off instead of climbing " +
                        "without limit, and levels off lower at high flow. A Duplicant is sent to clean the plates at 50% " +
                        "fouling.\n\n" +
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
                public static class PCHX_FOULING
                {
                    public static LocString NAME = "Fouling: {Fouling}";
                    public static LocString TOOLTIP =
                        "Deposits on the plates add thermal resistance. Heat transfer is down {Fouling} from clean.\n\n" +
                        "Deposits build up with flow but are also scoured away by it, and scouring grows faster than deposition. " +
                        "Fouling therefore levels off instead of climbing forever, and it levels off LOWER at high flow. " +
                        "Throttling a stream raises effectiveness but lets more deposit settle.\n\n" +
                        "Hot plates speed scaling (Brine, Salt Water) and coking (Crude Oil, Petroleum). " +
                        "Plates above 72 °C stop biological growth from Polluted Water. Water and Ethanol do not foul.\n\n" +
                        "A Duplicant is sent to clean at {Threshold}. Cleaning stops both streams and drops the deposits as debris.\n\n" +
                        "{Deposits}";
                }

                public static class PCHX_CLEANINGORDERED
                {
                    public static LocString NAME = "Cleaning ordered";
                    public static LocString TOOLTIP =
                        "A Duplicant will open the plate pack and remove the deposits. Both streams stop while the plates are open.";
                }

                public static class PCHX_NEEDSCLEANING
                {
                    public static LocString NAME = "Needs cleaning";
                    public static LocString TOOLTIP =
                        "Fouling is at {Fouling}, past the {Threshold} cleaning point, and no cleaning order is pending. " +
                        "Heat transfer keeps falling until the plates are cleaned.";
                }

                public static class PCHX_PORTSDISCONNECTED
                {
                    public static LocString NAME = "Pipe not connected";
                    public static LocString TOOLTIP =
                        "No liquid pipe at:\n{Ports}\n\nA stream with a missing port does not flow.";
                }

                public static class PCHX_PHASECHANGERISK
                {
                    public static LocString NAME = "Output near phase change";
                    public static LocString TOOLTIP =
                        "{Detail}\n\nA fluid that freezes or boils inside a pipe breaks the pipe. " +
                        "Throttle a stream, or bring the other stream's inlet closer in temperature.";
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
                    "Order a Duplicant to open the plate pack and remove deposits. Both streams stop during cleaning.";
                public static LocString CANCEL_CLEAN_BUTTON = "Cancel Cleaning";
                public static LocString CANCEL_CLEAN_BUTTON_TOOLTIP = "Withdraw the cleaning order.";

                // Stream and port names for tooltips (unrotated layout; see the config).
                public static LocString STREAM_A = "Stream A (bottom)";
                public static LocString STREAM_B = "Stream B (top)";
                public static LocString PORT_A_IN = "Stream A input (bottom-left)";
                public static LocString PORT_A_OUT = "Stream A output (bottom-right)";
                public static LocString PORT_B_IN = "Stream B input (top-right)";
                public static LocString PORT_B_OUT = "Stream B output (top-left)";

                // Deposit list: "{0}: {1}" per stream, and the word for an empty ledger.
                public static LocString DEPOSIT_LINE = "{0}: {1}";
                public static LocString NO_DEPOSITS = "clean";

                // {0} stream, {1} outlet temperature, {2} fluid name, {3} transition temperature.
                public static LocString PHASE_FREEZE = "{0} leaves at {1}; {2} freezes at {3}.";
                public static LocString PHASE_BOIL = "{0} leaves at {1}; {2} boils at {3}.";
            }
        }
    }
}
