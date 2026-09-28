using System;
using UnityEngine;

public enum GameSound
{
    Hover, Click, Open, Close, Confirm, Denied, StartGame,
    Footstep, Jump, Land, Slide, Stance, CameraSwitch,
    Rifle, SubmachineGun, Shotgun, RPG, Pistol, SniperRifle, MachineGun,
    Equip, Impact, EnemyHurt, EnemyDeath, Sword, Crossbow,
    GrenadePin, GrenadeThrow, GrenadeBounce, Fuse, Explosion,
    PlayerHurt, ArmorHit, ArmorBreak, PlayerDeath, Respawn, Heal, LowHealth,
    Pickup, Experience, Key, Coins, LevelUp, Upgrade, BuildReady,
    Unlock, RoomEnter, ArmorRestored, Teleport, BossSpawn, BossTransform, BossBurst, BossDeath,
    TrapWarning, SpikeRise, FloorCrack, FloorCollapse, Ambience
}

// Original synthesized effects. No external downloads or third-party audio licenses.
// The private PRNG deliberately never advances UnityEngine.Random's gameplay state.
public static class GameSoundBank
{
    public const int SampleRate = 22050;
    public static float Duration(GameSound sound)
    {
        switch (sound)
        {
            case GameSound.Ambience: return 8f;
            case GameSound.BossDeath: return 2.4f;
            case GameSound.BossSpawn: case GameSound.BossTransform: return 1.8f;
            case GameSound.Explosion: case GameSound.PlayerDeath: return 1.2f;
            case GameSound.FloorCollapse: return .9f;
            case GameSound.LevelUp: case GameSound.Respawn: case GameSound.StartGame: return .85f;
            case GameSound.Teleport: case GameSound.Unlock: return .75f;
            case GameSound.Heal: case GameSound.Upgrade: case GameSound.BuildReady: return .6f;
            case GameSound.Slide: case GameSound.BossBurst: return .5f;
            case GameSound.RPG: case GameSound.SniperRifle: case GameSound.Shotgun: return .45f;
            case GameSound.Hover: case GameSound.Click: case GameSound.Stance: return .09f;
            default: return .24f;
        }
    }

    public static float Gain(GameSound sound)
    {
        switch (sound)
        {
            case GameSound.Hover: return .10f;
            case GameSound.Click: case GameSound.Experience: return .18f;
            case GameSound.Fuse: case GameSound.RoomEnter: case GameSound.ArmorRestored: return .2f;
            case GameSound.Footstep: case GameSound.Stance: case GameSound.LowHealth: return .22f;
            case GameSound.Impact: case GameSound.EnemyHurt: case GameSound.GrenadeBounce: return .25f;
            case GameSound.Explosion: case GameSound.BossDeath: case GameSound.BossTransform: return .55f;
            default: return .38f;
        }
    }

    public static AudioClip Create(GameSound sound)
    {
        float[] samples = Synthesize(sound);
        AudioClip clip = AudioClip.Create("SFX " + sound, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public static float[] Synthesize(GameSound sound)
    {
        float duration = Duration(sound);
        var data = new float[Mathf.RoundToInt(duration * SampleRate)];
        var random = new System.Random(17041 + (int)sound * 997);
        double low = 0, previous = 0, phase = 0;
        for (int i = 0; i < data.Length; i++)
        {
            double t = (double)i / SampleRate, u = t / duration;
            double noise = random.NextDouble() * 2 - 1;
            low += .075 * (noise - low);
            double high = noise - previous; previous = noise;
            double value;
            if (sound >= GameSound.Rifle && sound <= GameSound.MachineGun)
            {
                double weight = sound == GameSound.Shotgun ? 1.25 : sound == GameSound.SniperRifle ? 1.5 :
                    sound == GameSound.SubmachineGun ? .65 : sound == GameSound.Pistol ? .9 : 1;
                value = .6 * noise * Math.Exp(-t * 55 / weight) +
                    .65 * Math.Sin(2 * Math.PI * (105 / weight * t + 18 * (1 - Math.Exp(-t * 35)))) * Math.Exp(-t * 22 / weight) +
                    low * 2 * Math.Exp(-t * 12 / weight);
                if (sound == GameSound.RPG) value = low * 3 * Math.Exp(-t * 7) + noise * .18 * Math.Sin(Math.PI * u);
                // A quieter mechanical tail separates the firearms from simple noise bursts.
                value += .16 * high * Math.Exp(-Math.Abs(t - .07) * 180);
            }
            else switch (sound)
            {
                case GameSound.Ambience:
                    value = low * .35 + .035 * Math.Sin(2 * Math.PI * 48 * t) +
                        .018 * Math.Sin(2 * Math.PI * 72 * t) * (.5 + .5 * Math.Sin(2 * Math.PI * t / duration));
                    break;
                case GameSound.Footstep: case GameSound.Land: case GameSound.GrenadeBounce: case GameSound.FloorCrack:
                    value = low * 2.1 * Math.Exp(-t * 25) + .18 * high * Math.Exp(-t * 65) +
                        .3 * Math.Sin(2 * Math.PI * (sound == GameSound.GrenadeBounce ? 620 : 92) * t) * Math.Exp(-t * 35);
                    break;
                case GameSound.Slide: case GameSound.Stance:
                    value = (.7 * low + .12 * noise) * Math.Sin(Math.PI * u) * (1 - u);
                    break;
                case GameSound.Jump: case GameSound.GrenadeThrow: case GameSound.Sword:
                    value = noise * .3 * Math.Pow(Math.Sin(Math.PI * u), 2) +
                        .25 * Math.Sin(2 * Math.PI * (180 * t - 200 * t * t)) * Math.Sin(Math.PI * u);
                    break;
                case GameSound.Explosion: case GameSound.BossSpawn: case GameSound.BossTransform:
                case GameSound.BossDeath: case GameSound.PlayerDeath: case GameSound.BossBurst: case GameSound.FloorCollapse:
                    double fall = sound == GameSound.BossTransform ? 70 : 120;
                    phase += 2 * Math.PI * (38 + fall * Math.Exp(-t * 5)) / SampleRate;
                    value = (2.4 * low + .35 * Math.Sin(phase) + .25 * noise * Math.Exp(-t * 12)) * Math.Exp(-u * 4);
                    if (sound != GameSound.Explosion)
                        value += .18 * Math.Sin(phase * 1.51) * Math.Sin(Math.PI * u) + .12 * Math.Sin(phase * 2.02) * (1 - u);
                    break;
                case GameSound.ArmorHit: case GameSound.ArmorBreak: case GameSound.Impact:
                case GameSound.Equip: case GameSound.GrenadePin: case GameSound.Crossbow: case GameSound.SpikeRise:
                    double metal = sound == GameSound.ArmorBreak ? 480 : sound == GameSound.Crossbow ? 180 : 1100;
                    value = (.35 * Math.Sin(2 * Math.PI * metal * t) + .22 * Math.Sin(2 * Math.PI * metal * 1.473 * t)) * Math.Exp(-t * 30) +
                        .24 * noise * Math.Exp(-t * 48) + .25 * low * Math.Exp(-t * 16);
                    break;
                case GameSound.EnemyHurt: case GameSound.PlayerHurt: case GameSound.EnemyDeath:
                    phase += 2 * Math.PI * (75 + 130 * (1 - u)) / SampleRate;
                    value = (.4 * Math.Sin(phase) + .3 * low + .16 * noise) * Math.Exp(-u * 5);
                    break;
                case GameSound.LowHealth:
                    value = Math.Sin(2 * Math.PI * 62 * t) *
                        (Math.Exp(-t * 50) + .6 * Math.Exp(-Math.Abs(t - .13) * 75));
                    break;
                default:
                    double frequency = NoteFrequency(sound, t);
                    phase += 2 * Math.PI * frequency / SampleRate;
                    double envelope = Math.Min(1, t / .006) * Math.Exp(-u * 3.5);
                    value = (Math.Sin(phase) * .55 + Math.Sin(phase * 2) * .12) * envelope;
                    if (sound == GameSound.Coins || sound == GameSound.Key)
                        value += .15 * Math.Sin(phase * 2.71) * envelope;
                    break;
            }
            // Taper both endpoints to avoid clicks. Ambience is seamless at its loop boundary.
            double fade = Math.Min(1, t / .003) * Math.Min(1, (duration - t) / (sound == GameSound.Ambience ? .2 : .018));
            if (sound == GameSound.Ambience) fade *= Math.Min(1, t / .2);
            data[i] = (float)(Math.Tanh(value * 1.3) * .8 * fade);
        }
        return data;
    }

    static double NoteFrequency(GameSound sound, double time)
    {
        switch (sound)
        {
            case GameSound.Hover: return 740;
            case GameSound.Click: return 520;
            case GameSound.Open: return 330 + time * 1400;
            case GameSound.Close: return 620 - time * 1400;
            case GameSound.Denied: return time < .12 ? 185 : 147;
            case GameSound.Fuse: return 1550;
            case GameSound.TrapWarning: return time < .12 ? 330 : 220;
            case GameSound.RoomEnter: return 110 + time * 100;
            case GameSound.ArmorRestored: return 660 + time * 500;
            case GameSound.Teleport: return 130 + time * 1100;
            case GameSound.Heal: return 440 + time * 450;
            case GameSound.LevelUp: case GameSound.Upgrade: case GameSound.Respawn:
            case GameSound.StartGame: case GameSound.Unlock: case GameSound.BuildReady:
                int note = Math.Min(3, (int)(time / .16));
                double register = sound == GameSound.Respawn ? .75 : sound == GameSound.StartGame ? .5 :
                    sound == GameSound.Unlock ? .67 : sound == GameSound.BuildReady ? 1.25 : sound == GameSound.Upgrade ? 1.12 : 1;
                return (note == 0 ? 392 : note == 1 ? 494 : note == 2 ? 587 : 784) * register;
            case GameSound.Experience: return 1046;
            case GameSound.Coins: return 1320;
            case GameSound.Key: return 880;
            case GameSound.CameraSwitch: return 280 + time * 600;
            case GameSound.Confirm: return time < .09 ? 523 : 784;
            default: return time < .09 ? 784 : 1046;
        }
    }
}
