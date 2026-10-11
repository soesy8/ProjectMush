# Lobby feeding audio

License verification: 2026-10-11, source pages accessed directly.
All three source collections explicitly identify these sounds as **CC0 1.0**.
License: https://creativecommons.org/publicdomain/zero/1.0/

| Project file | Original | Author | Source and license evidence |
| --- | --- | --- | --- |
| KibblePour.wav | Rice Pour, seconds 2.0–4.0 | thedapperdan | https://freesound.org/people/thedapperdan/sounds/199930/ — Creative Commons 0; description: rice poured into a plastic bucket |
| KibbleRattle.wav | first 0.32 s of that grain section | thedapperdan | Same source page and CC0 license |
| CanisterGrab.wav | Audio/impactWood_light_003.ogg | Kenney | https://kenney.nl/assets/impact-sounds — Creative Commons CC0; included License.txt also confirms CC0 |
| CanisterSetDown.wav | Audio/impactWood_light_001.ogg | Kenney | Same official page and included license |
| KibbleBowlImpact.wav | Audio/impactGeneric_light_002.ogg | Kenney | Same official page and included license |
| DogCrunch1.wav | crunch.1.ogg | Tito / StarNinjas | https://opengameart.org/content/7-eating-crunches — CC0 |
| DogCrunch3.wav | crunch.3.ogg | Tito / StarNinjas | Same source page and CC0 license |
| DogCrunch5.wav | crunch.5.ogg | Tito / StarNinjas | Same source page and CC0 license |

Download links actually used:

- Rice **public HQ MP3 preview**, not the login-gated original WAV: https://cdn.freesound.org/previews/199/199930_11337-hq.mp3
- Kenney original OGG collection: https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip
- Tito original OGG collection: https://opengameart.org/sites/default/files/crunch_-_tito.zip

Processing: downmix to mono, resample to 22,050 Hz, PCM16 WAV, short fades and peak limits. Grain loop attenuates low microphone rumble and brittle high frequencies. Crunch clips have leading/trailing silence removed. See audio-processing.json for durations and peaks. No metal, tin, plate, bell or collar sounds were selected.

Current art inspected in Unity: PM_FeedCanister_V3 is wood-like, Metallic 0, Smoothness 0.05; active 06_DogBowl materials have Metallic 0.15 and Smoothness 0.5, a glossy nonmetal appearance. Grain-on-plastic and soft generic impacts were selected rather than metal ringing. Suitability is based on source material descriptions and signal inspection; final perceptual balance requires an audible headset review.

Runtime: positional Effects bus, no doppler, quiet loop only while tilted, rate-limited grain/bowl/chewing responses. Three crunch variants avoid an identical repeated bite.
