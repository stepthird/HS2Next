// scene.json `scene`: map, screen effects (ScreenEffects.cs), camera, the
// character light, sound, background and frame. Lists HS2 stored as dropdown indices are `ref`s here.
using System.Collections.Generic;

namespace HS2Next.Save
{
    /// <summary>`scene`.</summary>
    public sealed class SceneSettings : DocObject
    {
        public MapSettings Map = new MapSettings();
        public ScreenEffects Effects = new ScreenEffects();
        /// <summary>(R)</summary>
        public CameraState Camera = new CameraState();
        public CharaLightSettings CharaLight = new CharaLightSettings();
        public SoundSettings Sound = new SoundSettings();
        /// <summary>Image drawn behind the scene; null = none.</summary>
        public FileRef Background;
        /// <summary>Full-screen overlay image; null = none.</summary>
        public FileRef Frame;

        internal override void ReadMembers(DocReader r)
        {
            Map.ReadFrom(r.Obj("map"));
            Effects.ReadFrom(r.Obj("effects"));
            Camera.ReadFrom(r.Obj("camera"));
            CharaLight.ReadFrom(r.Obj("charaLight"));
            Sound.ReadFrom(r.Obj("sound"));
            Background = ReadNew<FileRef>(r.ObjN("background"));
            Frame = ReadNew<FileRef>(r.ObjN("frame"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            Map.WriteTo(w, "map");
            Effects.WriteTo(w, "effects");
            Camera.WriteTo(w, "camera");
            CharaLight.WriteTo(w, "charaLight");
            Sound.WriteTo(w, "sound");
            WriteN(w, "background", Background);
            WriteN(w, "frame", Frame);
        }
    }

    /// <summary>`scene.map`: Map list row, map root pose, MapComponent option and light blocks.</summary>
    public sealed class MapSettings : DocObject
    {
        /// <summary>Null = no map.</summary>
        public string Ref;
        public Vec3 Pos = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rot = Vec3.Zero;
        public bool Options = true, Lights = true;

        internal override void ReadMembers(DocReader r)
        {
            Ref = r.RefN("ref");
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
            Options = r.Bool("options", Options);
            Lights = r.Bool("lights", Lights);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Ref("ref", Ref);
            w.V3("pos", Pos);
            w.Euler("rot", Rot);
            w.Bool("options", Options);
            w.Bool("lights", Lights);
        }
    }

    /// <summary>`scene.camera` (R): the current view and the slots (null = the app's default view / an empty slot).</summary>
    public sealed class CameraState : DocObject
    {
        public const int SlotCount = 10;

        public CameraView Current;
        /// <summary>Any length as read; absent = 10 × null.</summary>
        public List<CameraView> Slots = new List<CameraView>(new CameraView[SlotCount]);

        internal override void ReadMembers(DocReader r)
        {
            Current = ReadNew<CameraView>(r.ObjN("current"));
            if (r.Has("slots"))
            {
                var slots = r.Array("slots", v => v.AsObjN(), null);
                Slots = slots.ConvertAll(ReadNew<CameraView>);
            }
            else r.Consume("slots");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteN(w, "current", Current);
            w.StartArray("slots");
            foreach (var s in Slots ?? new List<CameraView>())
            {
                if (s == null) w.Null();
                else s.WriteTo(w);
            }
            w.EndArray();
        }
    }

    /// <summary>`Camera`: position = pivot + Euler(rot)·(0, 0, −distance). All four members are required (the schema
    /// gives no absent values).</summary>
    public sealed class CameraView : DocObject
    {
        public Vec3 Pivot;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rot;
        public float Distance, Fov;

        internal override void ReadMembers(DocReader r)
        {
            Pivot = r.V3("pivot");
            Rot = r.Euler("rot");
            Distance = r.F32("distance");
            Fov = r.F32("fov");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("pivot", Pivot);
            w.Euler("rot", Rot);
            w.F32("distance", Distance);
            w.F32("fov", Fov);
        }
    }

    /// <summary>`scene.charaLight`: the directional light under the camera, local Euler (rot[0], rot[1], 0).</summary>
    public sealed class CharaLightSettings : DocObject
    {
        public Color4 Color = Color4.White;
        public float Intensity = 1f;
        /// <summary>Exactly 2.</summary>
        public float[] Rot = { 0f, 0f };
        public bool Shadow = true;

        internal override void ReadMembers(DocReader r)
        {
            Color = r.Col("color", Color);
            Intensity = r.F32("intensity", Intensity);
            Rot = r.F32Array("rot", Rot, 2);
            Shadow = r.Bool("shadow", Shadow);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("color", Color);
            w.F32("intensity", Intensity);
            F32s(w, "rot", Rot, 2);
            w.Bool("shadow", Shadow);
        }
    }

    /// <summary>Repeat modes of BGM and user audio.</summary>
    public enum SoundRepeat { None, All }

    /// <summary>`scene.sound`: BGM and user audio share one channel.</summary>
    public sealed class SoundSettings : DocObject
    {
        public BgmSettings Bgm = new BgmSettings();
        public UserAudioSettings UserAudio = new UserAudioSettings();

        internal override void ReadMembers(DocReader r)
        {
            Bgm.ReadFrom(r.Obj("bgm"));
            UserAudio.ReadFrom(r.Obj("userAudio"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            Bgm.WriteTo(w, "bgm");
            UserAudio.WriteTo(w, "userAudio");
        }
    }

    /// <summary>`sound.bgm`.</summary>
    public sealed class BgmSettings : DocObject
    {
        /// <summary>BGM list row; null = none. Absent = illusion.hs2#0.</summary>
        public string Ref = DocDefaults.NoneRef;
        public bool Play;
        /// <summary>Enum string `none`/`all`.</summary>
        public string Repeat = "all";

        public SoundRepeat? RepeatType { get => DocEnum.ParseCamel<SoundRepeat>(Repeat); set => Repeat = DocEnum.Camel(value); }

        internal override void ReadMembers(DocReader r)
        {
            Ref = r.RefN("ref", Ref);
            Play = r.Bool("play", Play);
            Repeat = r.EnumStr("repeat", Repeat, DocEnum.CamelNamesOf<SoundRepeat>());
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Ref("ref", Ref);
            w.Bool("play", Play);
            ReqStr(w, "repeat", Repeat);
        }
    }

    /// <summary>`sound.userAudio`.</summary>
    public sealed class UserAudioSettings : DocObject
    {
        /// <summary>Null = none.</summary>
        public FileRef File;
        public bool Play;
        /// <summary>Enum string `none`/`all`.</summary>
        public string Repeat = "all";

        public SoundRepeat? RepeatType { get => DocEnum.ParseCamel<SoundRepeat>(Repeat); set => Repeat = DocEnum.Camel(value); }

        internal override void ReadMembers(DocReader r)
        {
            File = ReadNew<FileRef>(r.ObjN("file"));
            Play = r.Bool("play", Play);
            Repeat = r.EnumStr("repeat", Repeat, DocEnum.CamelNamesOf<SoundRepeat>());
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteN(w, "file", File);
            w.Bool("play", Play);
            ReqStr(w, "repeat", Repeat);
        }
    }
}
