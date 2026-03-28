using System;

namespace Hophesmoverlay
{
    // The list of behaviors matching your buttons
    public enum BehaviorType
    {
        // ELIMINATIONS
        SaltStep,           // Kills Wraith
        BreakerOff,         // Kills Jinn
        Airball,            // Kills Oni
        MaleGhost,
        FemaleGhost,

        // VISUAL CONFIRMATIONS
        HantuBreath,
        ObakeShape,
        PhantomBlink,
        OniBlink,
        DeogenWallhack,

        // AUDIO/SPEED CONFIRMATIONS
        MylingQuiet,
        YokaiDeaf,
        ThayeAging,
        RaijuElectronics,
        GalluVariable,
        ObamboVariable,

        // INTERACTIONS
        PoltyThrow,
        YureiSlam,
        MareLightTrap,
        PhantomPhoto,

        // TESTS
        BansheeScream,
        OnryoCandle,
        MimicOrbs,
        DemonSmudge,
        SpiritSmudge,

        // BEHAVIOR
        ShadeShy,
        GoryoRoam,
        MoroiCurse,
        TwinsInteraction,
        MylingFlicker,      // Flashlight flickers but silent steps (Confirms Myling)
        GalluDoubleSalt,    // Stepped in salt twice (Eliminates Gallu)
        MoroiBlind,         // Smudge blindness lasted > 7s (Confirms Moroi)
        LightOn,            // Turned light ON (Eliminates Mare)
        BreakerOn,          // Turned breaker ON (Eliminates Hantu)
        JinnBreakerAbility, // Sanity drop at breaker (Confirms Jinn)
        WraithTeleport,     // EMF 2/5 at your feet (Confirms Wraith)
        ShadeSinging,       // Ghost sang/hissed (Eliminates Shade) - *See note below
        OniAirball,         // Ghost did airball (Eliminates Oni)

        YureiSmudgeTrap,     // Smudged outside hunt -> Didn't leave room for 90s
        OnryoIgnoredCandle,  // Hunted while candle was lit (Eliminates Onryo)
        TwinsDecoy,          // Interactions in two places at once
        PoltyHuntPile,       // Exploded a pile of items during hunt
        PhantomRoam,         // Walked to player position (outside hunt)
        RevSlow,             // Very slow when not seeing player (Confirms Rev/Dayan)
    }

    public static class GhostRules
    {
        public static bool Check(string ghostID, BehaviorType behavior)
        {
            switch (behavior)
            {
                case BehaviorType.MaleGhost: return ghostID != "Banshee" && ghostID != "Dayan";
                case BehaviorType.FemaleGhost: return ghostID == "Banshee" ||  ghostID == "Dayan";
                // --- ELIMINATION RULES (If checked, these ghosts are GONE) ---
                case BehaviorType.SaltStep: return ghostID != "Wraith";
                case BehaviorType.BreakerOff: return ghostID != "Jinn";
                case BehaviorType.Airball: return ghostID != "Oni";

                // --- CONFIRMATION RULES (If checked, ONLY these ghosts remain) ---
                case BehaviorType.HantuBreath: return ghostID == "Hantu";
                case BehaviorType.ObakeShape: return ghostID == "Obake";
                case BehaviorType.PhantomBlink: return ghostID == "Phantom";
                case BehaviorType.OniBlink: return ghostID == "Oni";
                case BehaviorType.DeogenWallhack: return ghostID == "Deogen";

                case BehaviorType.MylingQuiet: return ghostID == "Myling";
                case BehaviorType.YokaiDeaf: return ghostID == "Yokai";
                case BehaviorType.ThayeAging: return ghostID == "Thaye";
                case BehaviorType.RaijuElectronics: return ghostID == "Raiju";
                case BehaviorType.GalluVariable: return ghostID == "Gallu";
                case BehaviorType.ObamboVariable: return ghostID == "Obambo";

                case BehaviorType.PoltyThrow: return ghostID == "Poltergeist";
                case BehaviorType.YureiSlam: return ghostID == "Yurei";
                case BehaviorType.MareLightTrap: return ghostID == "Mare";
                case BehaviorType.PhantomPhoto: return ghostID == "Phantom";

                case BehaviorType.BansheeScream: return ghostID == "Banshee";
                case BehaviorType.OnryoCandle: return ghostID == "Onryo";
                case BehaviorType.MimicOrbs: return ghostID == "The Mimic";
                case BehaviorType.DemonSmudge: return ghostID == "Demon";
                case BehaviorType.SpiritSmudge: return ghostID == "Spirit";
                case BehaviorType.MylingFlicker: return ghostID == "Myling";



                case BehaviorType.MoroiBlind:
                    return ghostID == "Moroi";

                case BehaviorType.JinnBreakerAbility:
                    return ghostID == "Jinn";

                case BehaviorType.WraithTeleport:
                    return ghostID == "Wraith";

                case BehaviorType.GalluDoubleSalt:
                    return ghostID != "Gallu"; // Gallu CANNOT step twice.

                case BehaviorType.LightOn:
                    return ghostID != "Mare"; // Mare CANNOT turn lights on.

                case BehaviorType.BreakerOn:
                    return ghostID != "Hantu"; // Hantu CANNOT turn breaker on.

                case BehaviorType.ShadeSinging:
                    return ghostID != "Shade"; // Shades (almost) never sing in full events.

                case BehaviorType.OniAirball:
                    return ghostID != "Oni"; // Oni CANNOT do the airball mist event.

                case BehaviorType.GoryoRoam:
                    return ghostID != "Goryo"; // Goryo CANNOT change favorite room.
                // --- NEW BEHAVIOR LOGIC ---

                case BehaviorType.ShadeShy:
                    return ghostID == "Shade";

                case BehaviorType.MoroiCurse:
                    return ghostID == "Moroi";

                case BehaviorType.TwinsInteraction:
                    return ghostID == "The Twins";

                // --- NEW PRO CONFIRMATIONS ---
                case BehaviorType.YureiSmudgeTrap:
                    return ghostID == "Yurei";

                case BehaviorType.TwinsDecoy:
                    return ghostID == "The Twins";

                case BehaviorType.PoltyHuntPile:
                    return ghostID == "Poltergeist";

                case BehaviorType.PhantomRoam:
                    return ghostID == "Phantom";

                case BehaviorType.RevSlow:
                    // Winter's Jest: Dayan mimics Revenant speed logic
                    return ghostID == "Revenant" || ghostID == "Dayan";

                // --- NEW ELIMINATIONS ---
                case BehaviorType.OnryoIgnoredCandle:
                    // Onryo MUST blow out candle before hunting. 
                    // If it hunted with candle lit, it's not Onryo.
                    return ghostID != "Onryo";


                default: return true;
            }
        }
    }
}