# Placeholder art (replace before release)

| Asset | Used for | Why it is a placeholder | Replacement |
| --- | --- | --- | --- |
| `Animals/animal-dog.fbx` (Kenney Cube Pets, CC0) | Dog variant of the parked-truck scenario | Blocky toy style does not match the city or vehicles | The team artist is making a dog model. Swap the model; keep a dog (the beaver's lines say "dog"). |
| `Vehicles/delivery.fbx` (Kenney Car Kit, CC0) | Parked box truck in the parked-truck scenario | Toy-scale model, scaled non-uniformly (1.6, 2.0, 2.4) to about 2.4 × 3.3 × 7.8 m | Optional. If replaced, keep it about 3.3 m tall with a gap under the body, and keep its front at u 73.5. |
| `Prefabs/Character.prefab` as children | School zone sidewalk (spec: 6 children and 1 adult) | No child model exists, so all 7 are the adult character | A child model (also needed for the playground and school bus scenarios). |
| None (playground area) | Playground scenario, left side z 62-108 | Only a fence marks the play area; the spec has a sand area, swing set and slide | Playground equipment model(s). The child runs out through the fence gap at z 84-92; keep a gap there. |
| Headliner, sun visors, dome light (built in code: `DrivingPractice.Roof.cs`) | Inside the player's car | The sedan model's roof is single-sided (faces the sky), so it is invisible from the driver's seat; code adds a simple interior shaped to the roof | A sedan model with a proper interior roof (headliner, visors, dome light, grab handles). Then delete `DrivingPractice.Roof.cs`. The sedan's source and licence are also unconfirmed. |
| `Resources/Beaver/Beaver_Nov23.fbx` | Passenger seat, and the live portrait on cards and captions | Static model: no rig or blendshapes, so it only bobs, tilts and blinks (eye meshes squashed) |  To ask the artist: blendshapes or a separate mouth/jaw, and named parts. |

Kenney assets are CC0 (see the licence files beside them). Crediting Kenney is optional.
