# Vehicle system

Vehicles are real, saved world entities that can be found, unlocked or hotwired, driven, refuelled, repaired, loaded with cargo, parked and found again after a reload.

## Types (Resources/Content/vehicles.json)
| Id | Category | Drive | Tank | Use | Doors | Trunk | How it starts in the valley |
|---|---|---|---|---|---|---|---|
| `sedan` | Car | front-wheel | 45 L, 0.08 L/km | 130 km/h, 1350 kg | 4 | 12 slots, 120 kg | Locked beside Millbrook Apartments; its key ("Blue sedan · MB-2187") is in flat 2's kitchen cabinet. A fuel can in the trunk. |
| `pickup` | Pickup | 4x4 | 70 L, 0.12 L/km | 115 km/h, 1850 kg | 2 | 24 slots, 400 kg | Unlocked at Hartley's Workshop, key lost: hotwire it. Scrap and parts in the bed. |
| `atv` | Quad bike | 4x4 | 12 L, 0.045 L/km | 75 km/h, 330 kg | 0 | 6 slots, 45 kg (rack) | At Dawson Farm with its key in the ignition and 2.5 L of fuel. |

In Greater Millbrook (world version 2) three more wait in the region: a locked grey sedan in Eastgate ("Grey sedan · EG-7730", its key in the Eastgate Apartments' flat 2 kitchen), a pickup on an Ashford street with its key in the ignition, and a quad bike in Pinecrest with 6 L of fuel. Roads there are graded for driving (tested: no 4 m stretch rises more than 0.35 m per metre) and two bridges cross the Kessel.

Each definition sets mass, torque, brakes, top speed, steering lock, fuel use, seats, doors, trunk size, body size and wheel layout, hotwiring time and difficulty, engine noise and paint options.

## Domain (Domain/Vehicles, Services/VehicleService.cs)
`VehicleState` (saved in `WorldDto.Vehicles`): position and yaw, paint, fuel, engine/body/tyres/battery condition (0–100), lock and lock name, key in the ignition, hotwired, smashed window, engine on, doors open, known (shown on the map), claimed (the settlement's), odometer, trunk inventory, creative-spawned flag and layout seed key. `PlayerState.VehicleId` records who is at the wheel, so a game saved while driving reloads at the wheel.

Rules (`VehicleRules`), all with a reason when they refuse:
- **Getting in**: a locked vehicle opens only with its key (a `key` item tagged with the vehicle's lock name); otherwise smash a window (X held for a second: loud, a little body damage, the doors can no longer be locked).
- **Starting**: needs its key, the key in the ignition, or a hotwired ignition; a battery above 5 %, an engine above 10 % and fuel.
- **Hotwiring** (an abstract skill check, no real-world procedure): hold F at the wheel for the vehicle's hotwiring time (5–8 s). It needs a screwdriver and uses a length of copper wire per attempt. The chance (10–95 %) improves with the Electrification technology, a charged battery and a sound engine, and drops with the vehicle's difficulty. Success lets it start without a key from then on; failure sparks, sets the horn off (a 32 m noise the infected follow) and chips the engine and battery. Rolled from the saved session random stream, so it is deterministic.
- **Fuel**: litres = km × L/km × (0.6 + 0.4 × throttle) × (1 + wear) + idling time. The engine stalls when the tank is empty. A fuel can adds 12 L.
- **Repairs** (hammer needed except for the battery): engine +40 % for 2 mechanical parts, body +35 % for 4 scrap metal, tyres +50 % for 1 part and 2 scrap, battery replaced with a battery.
- **Crashes**: impacts above 4 m/s damage the body, and the engine once the body is weak; a crash above 14 m/s hurts the driver.
- **The infected**: running one down kills it from about 25 km/h; infected attacking someone in a vehicle tear at its body instead until it gives way; a running engine is noise heard up to 22–30 m.
- **Getting out**: only below 3 m/s; the engine is switched off.

`VehicleSystem` (1 Hz) makes running engines noisy and lets an engine left running idle its fuel away.

## Presentation (Presentation/World/VehicleView.cs, Rendering/MeshFactory.Vehicles.cs)
- **Models**: sedan and pickup bodies reuse the wreck's lofted shell in good paint with light rust; the door openings are dark and the doors are separate hinged panels carrying their windows; seats, dashboard and steering wheel inside. The quad bike is built from parts (frame, fenders, tank, seat, racks, handlebars, lights). Wheels are separate meshes that turn and steer.
- **Physics**: a rigidbody on four `WheelCollider`s (Unity's built-in Vehicles module, now enabled in the manifest), lowered centre of mass, speed-sensitive steering, front-wheel or four-wheel drive, brakes and reverse, handbrake, downforce and a self-righting torque; a vehicle stuck on its side is set back on its wheels after a few seconds. Engine condition scales torque and tyre condition scales grip. Away from the survivor (over 90 m) a vehicle is kinematic and still.
- **Layer**: vehicles use a new `Vehicle` physics layer: solid for people, placement and interaction, but left out of the NavMesh bake and ignored by the camera. A parked vehicle carves the NavMesh with an obstacle so agents walk around it.
- **Driving**: E gets in (the driver's door swings open and the survivor sits at the wheel in a seated pose), W/S throttle and brake/reverse, A/D steer, Space handbrake, F engine on/off (hold F to hotwire), E gets out beside the driver's door (or the other side, or behind, wherever a person fits), R opens the vehicle window. A chase camera swings behind the vehicle when the mouse is left alone. A status line shows speed, fuel, the engine and how it starts, and condition.
- **Lights and engine**: two headlights (one on the quad bike) and red tail lights come on with the engine at dusk, at night and in rain or storms; L switches them between automatic, always on and off. The engine is a procedural loop whose pitch and volume follow the revs, with a starter sound when it catches. Bodies and paint are mapped in the vehicle's own space, so nothing of the ground shows on them.
- **Vehicle window** (R): condition and how it starts, refuel, repairs with their costs, lock and unlock with the key, claim for the settlement, open and close the doors, and the trunk beside the backpack.
- **Map**: vehicles the survivor has been in, inspected or claimed appear on the map and minimap ("yours" when claimed).
- **Persistence**: the moving vehicle's transform is written back while driven, and while it settles or is pushed when parked.

## Creative panel → Vehicles
A searchable list of every vehicle type with its specification; spawn options for fuel (0/25/100 %), condition (25/60/100 %), locked, "give me its key" and paint. Spawning finds clear, level ground ahead with a physics box test (never inside buildings, on steep slopes, in water or on the survivor). The nearest creative vehicle can be refuelled and repaired, moved in front of you or deleted (its trunk spills on the ground).

## Tests
- Headless (`VehicleTests`, 7): seeded placement and how each vehicle is reached; locked cars, keys and smashed windows; hotwiring needs, chance, consumption, consequences and determinism; fuel burn, stalling and refuelling; crashes, the infected and repairs; save and load with the trunk, keys and the driver; creative spawn, reset and delete.
- In the build (tour `VehicleScene`): the sedan is locked; its key is found in the apartments; get in seated; start; drive more than 100 m along the road to the gas station (fuel and odometer change, upright); get out on safe ground; lock it; save, reload and find it where it was parked with the same fuel, locked, key in the backpack; get into the pickup, fail to start it, hotwire it, start it, run down an infected; ride the quad bike; spawn a pickup from the creative panel.

## Not done yet
Passengers and colonists driving, motorcycles, bicycles, trucks, buses and tractors, tyre sounds, vehicle fuel from the gas station's pumps, damage visuals, towing. (Routes by road to the waypoint are in MAP_SYSTEM.md.)
