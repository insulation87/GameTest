using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Original D-minor themes, rendered offline so gameplay never synthesizes long tracks.
public static class GameMusicBuilder
{
    const int Rate = 22050, Bars = 16;
    enum Instrument { Pad, Pluck, Bass, Strings, Bell, Kick, Snare, Hat }
    static readonly int[] Roots = { 50, 46, 53, 48, 50, 46, 43, 45 };
    static readonly int[] Thirds = { 3, 4, 4, 4, 3, 4, 3, 4 };

    [MenuItem("Tools/Audio/Rebuild Background Music")]
    public static void Build()
    {
        string output = "Assets/Script/Audio/Resources/Music";
        Directory.CreateDirectory(output);
        foreach (MusicTheme theme in Enum.GetValues(typeof(MusicTheme)))
        {
            if (theme == MusicTheme.None) continue;
            string path = output + "/" + theme + ".wav";
            float[] samples = Compose(theme);
            WriteWave(path, samples);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .7f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
            Debug.Log($"MUSIC_RENDERED {theme}: {samples.Length / (Rate * 2f):F2}s, stereo {Rate} Hz");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("MUSIC_BUILD_OK: five original looping scores.");
    }

    public static float[] Compose(MusicTheme theme)
    {
        if (theme == MusicTheme.None) throw new ArgumentException("None has no score.");
        double bpm = theme == MusicTheme.Menu ? 80 : theme == MusicTheme.Dungeon ? 60 :
            theme == MusicTheme.Boss1 ? 100 : theme == MusicTheme.Boss2 ? 120 : 136;
        double beat = 60 / bpm;
        int frames = (int)Math.Round(Bars * 4 * beat * Rate);
        var data = new float[frames * 2];
        int intensity = theme == MusicTheme.Boss1 ? 1 : theme == MusicTheme.Boss2 ? 2 : theme == MusicTheme.Boss3 ? 3 : 0;
        var random = new System.Random(8731 + (int)theme * 41);
        for (int bar = 0; bar < Bars; bar++)
        {
            int chord = bar / 2, root = Roots[chord], third = Thirds[chord];
            double start = bar * 4;
            // Long, overlapping strings and an open fifth hold the harmony across bar lines.
            if (bar % 2 == 0)
            {
                Note(data, start * beat, root, 8 * beat, Instrument.Pad, .052, -.55, random);
                Note(data, start * beat, root + 7, 8 * beat, Instrument.Pad, .036, .55, random);
                Note(data, start * beat, root + 12 + third, 8 * beat, Instrument.Pad, .024, .1, random);
            }
            for (int pulse = 0; pulse < 4; pulse++)
            {
                if (theme == MusicTheme.Dungeon && pulse % 2 != 0) continue;
                Note(data, (start + pulse) * beat, root - 12, beat * .82, Instrument.Bass,
                    intensity > 0 ? .115 : .055, 0, random);
            }
            int[] arpeggio = { 0, 7, 12, third, 7, 12, third, 7 };
            int subdivisions = intensity == 3 ? 4 : theme == MusicTheme.Dungeon ? 1 : 2;
            for (int step = 0; step < 4 * subdivisions; step++)
            {
                int interval = arpeggio[(step + (bar % 2) * 2) % arpeggio.Length];
                Note(data, (start + (double)step / subdivisions) * beat, root + 12 + interval,
                    beat * (intensity > 0 ? .34 : .85), intensity > 0 ? Instrument.Strings : Instrument.Pluck,
                    intensity > 0 ? .047 : theme == MusicTheme.Menu ? .07 : .036,
                    step % 2 == 0 ? -.3 : .3, random);
            }
            // A separate four-note answer develops in the second half of each phrase.
            if (theme == MusicTheme.Menu || intensity >= 2 || (theme == MusicTheme.Dungeon && bar % 4 == 2))
            {
                int[] melody = bar < 8 ? new[] { 7, 12, third + 12, 12 } : new[] { third + 12, 14, 12, 7 };
                for (int note = 0; note < 4; note++)
                    Note(data, (start + note) * beat, root + 12 + melody[note], beat * .9,
                        intensity > 0 ? Instrument.Strings : Instrument.Bell,
                        intensity > 0 ? .05 : .026, .18, random);
            }
            if (intensity > 0)
            {
                for (int step = 0; step < 8; step++)
                {
                    double when = (start + step * .5) * beat;
                    Note(data, when, 0, .12, Instrument.Hat, step % 2 == 0 ? .03 : .016, -.35, random);
                    if (step == 0 || step == 4 || (intensity >= 2 && step == 7) || (intensity == 3 && step == 3))
                        Note(data, when, 0, .35, Instrument.Kick, .2, 0, random);
                    if (step == 2 || step == 6)
                        Note(data, when, 0, .22, Instrument.Snare, .075, .18, random);
                }
                if (bar % 4 == 3)
                    for (int fill = 0; fill < intensity + 1; fill++)
                        Note(data, (start + 3 + fill * .25) * beat, 0, .2, Instrument.Snare, .045, -.2, random);
            }
            else if (theme == MusicTheme.Dungeon && bar % 2 == 0)
                Note(data, start * beat, 0, .55, Instrument.Kick, .09, 0, random);
        }
        // Circular reflections preserve the reverb tail at the loop boundary.
        var dry = (float[])data.Clone();
        int delayA = (int)(Rate * .227), delayB = (int)(Rate * .389);
        float peak = 0;
        for (int i = 0; i < frames; i++)
        {
            int a = (i - delayA + frames) % frames, b = (i - delayB + frames) % frames;
            for (int channel = 0; channel < 2; channel++)
            {
                int index = i * 2 + channel;
                data[index] += dry[a * 2 + 1 - channel] * .18f + dry[b * 2 + channel] * .11f;
                peak = Math.Max(peak, Math.Abs(data[index]));
            }
        }
        float normalize = .82f / Math.Max(.001f, peak);
        for (int i = 0; i < data.Length; i++) data[i] *= normalize;
        return data;
    }

    static void Note(float[] data, double start, int midi, double duration, Instrument instrument,
        double volume, double pan, System.Random random)
    {
        int frames = data.Length / 2, first = (int)Math.Round(start * Rate);
        double frequency = 440 * Math.Pow(2, (midi - 69) / 12.0);
        double release = instrument == Instrument.Pad ? 1.2 : instrument == Instrument.Bell ? .6 : .08;
        int length = (int)((duration + release) * Rate);
        double left = Math.Sqrt((1 - pan) * .5), right = Math.Sqrt((1 + pan) * .5);
        double phase = 0, low = 0;
        for (int sample = 0; sample < length; sample++)
        {
            double t = (double)sample / Rate;
            double attack = instrument == Instrument.Pad ? .4 : .006;
            double envelope = Math.Min(1, t / attack) * Math.Min(1, (duration + release - t) / release);
            double value;
            phase += 2 * Math.PI * frequency / Rate;
            switch (instrument)
            {
                case Instrument.Pad:
                    value = (Math.Sin(phase) + .3 * Math.Sin(phase * 2) + .18 * Math.Sin(phase * 1.002)) * .65;
                    break;
                case Instrument.Strings:
                    value = (Math.Sin(phase) + .38 * Math.Sin(phase * 2) + .2 * Math.Sin(phase * 3) + .08 * Math.Sin(phase * 5)) * .6;
                    envelope *= .8 + .2 * Math.Sin(t * 32);
                    break;
                case Instrument.Pluck:
                    value = Math.Sin(phase) + .35 * Math.Sin(phase * 2) * Math.Exp(-t * 8);
                    envelope *= Math.Exp(-t * 5);
                    break;
                case Instrument.Bell:
                    value = Math.Sin(phase) + .25 * Math.Sin(phase * 3.01) * Math.Exp(-t * 4);
                    envelope *= Math.Exp(-t * 2.6);
                    break;
                case Instrument.Bass:
                    value = Math.Sin(phase) + .22 * Math.Sin(phase * 2);
                    break;
                case Instrument.Kick:
                    value = Math.Sin(2 * Math.PI * (48 * t + 4.2 * (1 - Math.Exp(-t * 24)))) * Math.Exp(-t * 11);
                    break;
                default:
                    double noise = random.NextDouble() * 2 - 1;
                    low += .15 * (noise - low);
                    value = instrument == Instrument.Hat ? (noise - low) * Math.Exp(-t * 45) :
                        (noise * .6 + Math.Sin(2 * Math.PI * 170 * t) * .4) * Math.Exp(-t * 22);
                    break;
            }
            int frame = (first + sample) % frames;
            data[frame * 2] += (float)(value * envelope * volume * left);
            data[frame * 2 + 1] += (float)(value * envelope * volume * right);
        }
    }

    static void WriteWave(string path, float[] samples)
    {
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(Rate); writer.Write(Rate * 4);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float value in samples) writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, value)) * 32767));
        }
    }
}
