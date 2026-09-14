# Oni-PlateCFHX
A Plate Counterflow Heat Exchanger for Oxygen Not Included: it moves heat from
one liquid pipeline into another, using no power.

## Contents
- [What it is](#what-it-is)
- [Why you want one](#why-you-want-one)
- [Picking a metal](#picking-a-metal)
- [Choosing a flow rate](#choosing-a-flow-rate)
- [Cleaning the plates](#cleaning-the-plates)
- [Where to find it and what it costs](#where-to-find-it-and-what-it-costs)
- [Freezing and boiling warnings](#freezing-and-boiling-warnings)
- [More detail](#more-detail)

## What it is
A 3x3 building with four liquid pipe connections and no power input. It carries
two separate streams that never mix and never store liquid; whatever goes in one
side comes straight out the other side of the same stream, at a new temperature.

The two streams run in opposite directions, which is what "counterflow" means.
One stream uses the bottom row: input bottom-left, output bottom-right. The
other uses the top row: input top-right, output top-left. Both streams have to
be flowing for heat to move. If one side stalls, the other side just acts like a
length of pipe for that moment.

The plates melt if you run a liquid hotter than the construction metal can
survive, insulation or not.

## Why you want one
In game, it hands heat from a stream you want cooler to a stream you want
warmer, for free. Warm your cold incoming water with the hot water leaving a
process. Recover heat you would otherwise pay an Aquatuner to remove, or preheat
a loop you would otherwise heat to feed a Steam Turbine. No power, no Duplicant
attention except the occasional cleaning.

Real plants use the same device everywhere: building heating, refineries, power
stations. Counterflow beats sending both streams the same direction because the
two fluids stay at a useful temperature difference along the entire length. That
is also why a counterflow exchanger can do something surprising: the cold
stream's outlet can leave hotter than the hot stream's outlet.

## Picking a metal
The construction metal sets how well the exchanger transfers heat. Only refined
metals are allowed; raw ores conduct too poorly to make a working exchanger.

Better conductors give higher effectiveness, but the gains flatten out. The best
metals, aluminum and thermium, sit near the top of the scale and are hard to
separate. Copper, gold, tungsten, iron and steel form a solid middle group; lead
is the weak end. Copper reaches roughly three quarters effectiveness at full
flow on water against brine, which is a good working exchanger.

Guidance:
- Build your first ones out of whatever refined metal you already smelt. Copper,
  iron or steel all work.
- The metal matters most when you push full pipe flow through. If you throttle
  the exchanger anyway, a cheap metal gets close to a good one.
- Upgrading to aluminum or thermium pays when the exchanger is running flat out
  and you want the last few percent of heat recovery.
- Better metals lose more of their performance, in percentage terms, to the same
  amount of fouling. A high-end exchanger needs cleaning attention sooner than a
  lead one does.

## Choosing a flow rate
Put a liquid valve on the inlet and you control the trade-off directly.

- Slower flow raises effectiveness. Each packet spends more time against the
  plates, so it comes out closer to the other stream's temperature. Throttle
  hard and almost any metal gets above 90 percent.
- Slower flow moves less total heat per second, because less liquid passes
  through. A stream at a fraction of full rate carries a fraction of the energy,
  even at high effectiveness.
- Slower flow fouls the plates faster. Fast flow scours deposits off; a
  throttled stream lets them settle and build to a much higher level.

So throttle when you care about the outlet temperature of a small stream, and
run wide open when you care about total heat moved. Check your work in the
building's Flow status tooltip: it names the ports, shows each stream's rate,
and shows the effectiveness of the last moment both streams were flowing. That
effectiveness figure is the one number that tells you what throttling bought you
and what fouling has cost you.

## Cleaning the plates
Liquids that carry something dissolved or suspended leave deposits on the
plates, and the deposits slow heat transfer. Brine and salt water scale up.
Oils and hydrocarbons coke. Polluted water grows a film until it is hot enough
to pasteurize. Clean liquids like water leave nothing at all.

Fouling grows toward a ceiling and stops; it never fully blocks the exchanger.
Where the ceiling sits depends on the liquid, the plate temperature and the flow
rate. Full-flow brine on copper settles around a quarter fouled and stays there.

The building shows a fouling percentage, and at 50 percent it raises a cleaning
errand on its own. You can cancel it, or order a clean at any time from the
building's panel.

- Cleaning stops both streams while the plates are open. Budget about 30 seconds
  of Duplicant work plus walking time, and expect the input pipes to back up
  behind it like a closed valve.
- The deposits come out as debris of the real material: salt from brine, dirt
  from polluted water, refined carbon from crude oil. Some players will run an
  exchanger as a slow, free source of that material.

When to cancel: if the exchanger is running wide open and already sitting at its
ceiling, the loss is small and permanent, so a clean buys little. Cancel and get
on with your day. When to clean early: before a run where the outlet temperature
matters, and on high-end metals, where a small deposit costs a large share of
performance. A low-conductivity metal on an ordinary liquid may never reach the
50 percent mark at all, so those exchangers can be left alone.

## Where to find it and what it costs
Under Utilities, in the Liquid Tuning group, right after the Aquatuner. The
Liquid Tuning research unlocks it, the same tech that gives you the Aquatuner
and the Conduction Panel.

Three material slots:
- **Refined metal** for the plates. This is the choice that sets performance.
- **Two Plastic Gaskets.** Cleaning means opening the plate pack, and a plate
  pack needs gaskets.
- **An insulating material** for the shell, which sets how much heat leaks out
  of the building into the room. Insulite leaks nothing. Ceramic leaks a little.
  Refined Carbon is the cheap early option and leaks the most. Rubber and Pearl
  also qualify.

## Freezing and boiling warnings
A good exchanger can push a liquid all the way past its freezing or boiling
point, and the outlet pipe then breaks the way any pipe does. The building warns
you when an outlet leaves close to a phase change, before the pipe fails. Treat
that warning as a signal to throttle the flow, change the temperature you are
feeding it, or accept the exchanger will not be that effective here. The
building deliberately does not clamp the temperature for you.

## More detail
- [DEVELOPMENT.md](DEVELOPMENT.md): implementation, ports, and mod wiring.
- [THERMAL.md](THERMAL.md): the heat transfer model, per-metal effectiveness,
  the insulation options, and melting.
- [FOULING.md](FOULING.md): the fouling model and the full list of which liquids
  foul, what they leave behind, and how fast.
- [ENGINE.md](ENGINE.md): notes for modders on how the game's own code behaves.
