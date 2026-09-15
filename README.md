# Oni-PlateCFHX
This mod adds a Plate Counterflow Heat Exchanger building for Oxygen Not Included. This is a passive device that moves heat from one liquid pipeline into another, using no pumps or power to accelerate heat transfer.

## Contents
- [What it is](#what-it-is)
- [Why to build one](#why-to-build-one)
- [Choosing a metal](#choosing-a-metal)
- [Choosing a flow rate](#choosing-a-flow-rate)
- [Cleaning the plates](#cleaning-the-plates)
- [Where to find it and what it costs](#where-to-find-it-and-what-it-costs)
- [Freezing and boiling warnings](#freezing-and-boiling-warnings)
- [More detail](#more-detail)

## What it is
A 3x3 building with four liquid pipe connections and no power input. It carries two separate streams that never mix. The building stores no liquid, so whatever goes in one side passes directly through to the other side of the same stream at a new temperature.

The two streams run in opposite directions, which is what "counterflow" means. Stream A uses the bottom row, with input on bottom-left and output on bottom-right. Stream B uses the top row, with input on top-right and output on top-left. Both streams must be flowing for heat to move between them. If one stream stalls, no heat moves between the streams, and the flowing stream passes through like a length of pipe. However, even a single flowing stream still deposits fouling and still trades heat with the building's shell.

The building is insulated to slow heat transfer to the room where it is placed, but the thermal conduction plates will melt if you run a liquid hotter than the construction metal can survive.

## Why to build one
In game, it moves heat from a stream you want colder to a stream you want hotter, for free. For example, you can warm a cold incoming fluid with the hot water leaving a steam turbine. Recover heat you would otherwise pay an Aquatuner to remove, or preheat a loop you would otherwise heat to feed a Steam Turbine. While the building requires no power and no Duplicant operation, it does require periodic cleaning.

Counterflow heat exchangers are used extensively in the real world. Applications include building heating, refineries, pasteurization plants, and power stations. Counterflow geometry has the useful property that it allows two fluids to swap heat, meaning the cold stream outlet can leave hotter than the hot stream outlet. In contrast, a parallel flow heat exchanger brings two streams toward a uniform temperature.

It is possible to build a counterflow heat exchanger in the game using only in-game tiles, fluids, and heat transfer mechanisms, but effective ones are very large. This building achieves high effectiveness in a 3x3 footprint.

## Choosing a metal
The construction metal sets how well the exchanger transfers heat. Only refined metals are allowed, because raw ores conduct too poorly for effective heat exchange.

Better conductors give higher effectiveness, but the gains flatten out. Aluminum and thermium are the most effective metals, but thermium is a better choice for very hot flows because of its higher melting point. Copper, gold, tungsten, iron and steel are in the middle of the effectiveness range, but tungsten remains the best choice for extremely hot fluids because it melts at an even hotter temperature than thermium. Lead is the weakest option, but remains a viable early-game choice. As a reference point, copper reaches roughly three quarters effectiveness at full flow on water against brine, which is a good working exchanger.

Guidance:
- Build your first heat exchangers out of whatever refined metal you already have. Copper, iron, or steel all work well.
- The metal matters most at the maximum 10 kg/s pipe flow. Because effectiveness increases as flow rate decreases, a cheap metal gets close to a good one at lower flow rates.
- Upgrading to aluminum or thermium is worthwhile when the exchanger is running flat out and you want the last few percent of heat recovery.
- Better metals lose more of their performance (in percentage terms) to the same amount of fouling (see below). A high-end exchanger needs cleaning attention sooner than a lead one does.

## Choosing a flow rate
You can control the flow rate through the exchanger with a liquid valve on the inlet (or outlet) pipe.

- Slower flow raises effectiveness. Each packet spends more time against the plates, so it comes out closer to the other stream's temperature. At low flow rates, almost any metal gets above 90% effectiveness.
- Slower flow moves less total heat per second, because less liquid passes through. A stream at a fraction of full rate carries a fraction of the energy, even at high effectiveness.
- Slower flows foul the plates faster. Fast flows scour some of the deposits the same flow leaves behind. Throttled streams allow deposits to settle and build to a much higher level.

Throttle streams when your highest priority is outlet temperature, and
run at full flow rate when you want to prioritize total heat moved. The
building's Flow status tooltip names the two streams, shows each stream's rate, and shows the current effectiveness. Effectiveness reads "none (no flow)" whenever either stream is stopped or the plates are open for cleaning. The effectiveness figure is the one number that reflects the total impact of throttling and fouling.

## Cleaning the plates
Liquids that carry something dissolved or suspended leave deposits on the plates, which slows heat transfer. Brine and salt water leave scale deposits. Oils and hydrocarbons coke. Polluted water deposits a slimy layer of microorganisms until it is hot enough to pasteurize. Clean liquids like water leave nothing at all.

Fouling grows toward a ceiling and stops. It will never fully block the exchanger. The maximum impact of fouling depends on the liquid, the plate temperature, and the flow rate. Running brine at full flow rate through a copper heat exchanger settles around a quarter fouled and stays there.

The building status shows a fouling percentage, and at 50% it automatically raises a cleaning errand. You can cancel the automatic order and it will not refire until the plates have been cleaned. You can also order a cleaning errand from the building's panel once any deposit has formed.

- Cleaning stops both streams while the plates are open. Budget about 30 seconds of Duplicant work time (plus travel time), and expect the input pipes to back up behind it like a closed valve.
- The deposits come out as debris of the material modeled for each fluid's deposit (see [FOULING.md](FOULING.md), "Fluids and byproducts"). Brine and salt water produce salt, polluted water produces dirt, etc. Players can choose to run an exchanger as a slow, free source of that material.

When to cancel the cleaning job: if the exchanger is running at full flow rate and already is sitting at its effectiveness ceiling, the loss due to fouling is small and will not increase further, so a clean buys you little. In that case, simply cancel the automatic errand.

When to clean early: When the outlet temperature is your highest priority, and on high-end metals, where a small deposit costs a large share of performance. A low-conductivity metal on an ordinary liquid may never reach the 50% mark at all, so those exchangers can be left alone.

## Where to find it and what it costs
Under Utilities, in the Liquid Tuning group, right after the Aquatuner. The building is unlocked through Liquid Tuning research, the same tech that unlocks the Aquatuner and the Conduction Panel.

The heat exchanger requires three materials to build:
1. **Refined metal** for the plates. This choice determines overall performance.
2. **Two Gaskets.** Cleaning a plate heat exchanger in the real world means opening the plate pack. Gaskets in the build recipe model this physical reality.
3. **An insulating material** for the shell, which determines how much heat leaks out of the building into the surrounding environment. All solid materials with the insulator tag are valid choices. Refined carbon is a cheap early option, but it allows the most heat to escape.

## Freezing and boiling warnings
A good exchanger can push a liquid past its freezing or boiling
point. A packet larger than 1 kg then freezes or boils in the pipe and breaks it. A smaller packet passes through without changing state, so a throttled stream stays safe even when it leaves too cold or too hot. The building warns you when an outlet leaves close to a phase change, before the pipe fails. Treat that warning as a signal to throttle the flow or change the temperature of the counterflow stream. The warning reads the temperature only, so it also appears for small packets that will not change phase. The building deliberately does not clamp the temperature for you.

## More detail
- [THERMAL.md](THERMAL.md) describes the heat transfer model, per-metal effectiveness, insulation options, and melting.
- [FOULING.md](FOULING.md) describes the fouling model, the list of liquids that foul, what they leave behind, and how fast.

## Source and license
The source is on GitHub. Released under the [MIT License](LICENSE).
Feedback and bug reports are welcome as GitHub issues.
