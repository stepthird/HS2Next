// What the operations write into the two ragdoll blocks by name, Unity-free: the members of a character's `values`, of the scene's `values`, the room and the floor. The caller edits copies of
// the blocks and hands them to one edit command each.
//   a body's values   by the names of RagdollBodyValues
//   the world's       by the names of RagdollWorldValues
//   room              [x0, x1, z0, z1] (the floor strip of a build: the middle and the two lengths, the height as a build's) or {center, size}; room.center [x, z] or null, room.size [w, h, d]
//   floor             {mode, offset, height}, or floor.mode, floor.offset, floor.height
// Numbers are stored as given: the ranges act when a value is applied.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Ragdoll
{
    /// <summary>Which block a value's name belongs to.</summary>
    public enum RagdollValueBlock { None, Body, World }

    public static class RagdollEdits
    {
        public const string Room = "room", RoomCenter = "room.center", RoomSize = "room.size";
        public const string Floor = "floor", FloorMode = "floor.mode", FloorOffset = "floor.offset", FloorHeight = "floor.height";
        /// <summary>The height of a room a build gives as a floor strip.</summary>
        public const float BuildRoomHeight = 40f;

        static readonly string[] other = { Room, RoomCenter, RoomSize, Floor, FloorMode, FloorOffset, FloorHeight };

        /// <summary>The names that can be set, for a refusal's text: the body's, the world's, the room's and the floor's.</summary>
        public static List<string> Names()
        {
            var l = new List<string>(RagdollBodyValues.Names);
            l.AddRange(RagdollWorldValues.Names);
            l.AddRange(other);
            return l;
        }

        static bool Has(IReadOnlyList<string> list, string name)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == name) return true;
            return false;
        }

        /// <summary>The block <paramref name="name"/> belongs to.</summary>
        public static RagdollValueBlock BlockOf(string name)
        {
            if (Has(RagdollBodyValues.Names, name)) return RagdollValueBlock.Body;
            if (Has(RagdollWorldValues.Names, name) || Array.IndexOf(other, name) >= 0) return RagdollValueBlock.World;
            return RagdollValueBlock.None;
        }

        /// <summary>
        /// Sets <paramref name="name"/> on the copy of its block; <paramref name="body"/> may be null for a name of the world. Returns what stops it, or null; <paramref name="changed"/> says
        /// whether the block changed.
        /// </summary>
        public static string Set(CharaRagdoll body, SceneRagdollWorld world, string name, JToken value, out bool changed)
        {
            changed = false;
            switch (BlockOf(name))
            {
                case RagdollValueBlock.Body:
                {
                    if (body == null) return name + " is a value of a character";
                    var before = body.Values.Get(name);
                    string why = body.Values.Set(name, value);
                    changed = why == null && !Equals(before, body.Values.Get(name));
                    return why;
                }
                case RagdollValueBlock.World:
                {
                    if (!Has(RagdollWorldValues.Names, name)) return SetPlace(world, name, value, out changed);
                    var before = world.Values.Get(name);
                    string why = world.Values.Set(name, value);
                    changed = why == null && !Equals(before, world.Values.Get(name));
                    return why;
                }
                default:
                    return "no value " + name;
            }
        }

        static string SetPlace(SceneRagdollWorld world, string name, JToken value, out bool changed)
        {
            var room = world.Room;
            var floor = world.Floor;
            var center = room.Center;
            var size = room.Size;
            string mode = floor.Mode;
            float offset = floor.Offset, height = floor.Height;
            string why;
            switch (name)
            {
                case Room: why = SetRoom(room, value); break;
                case RoomCenter: why = SetCenter(room, value); break;
                case RoomSize: why = SetSize(room, value); break;
                case Floor: why = SetFloor(floor, value); break;
                case FloorMode: why = SetMode(floor, value); break;
                case FloorOffset: why = Number(name, value, out floor.Offset); break;
                default: why = Number(name, value, out floor.Height); break;
            }
            if (why != null)
            {
                room.Center = center;
                room.Size = size;
                floor.Mode = mode;
                floor.Offset = offset;
                floor.Height = height;
            }
            changed = why == null && (!Nullable.Equals(center, room.Center) || !size.Equals(room.Size) || mode != floor.Mode || !offset.Equals(floor.Offset) || !height.Equals(floor.Height));
            return why;
        }

        static string Number(string name, JToken t, out float value)
        {
            value = 0f;
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return name + " is a number";
            value = t.Value<float>();
            return float.IsNaN(value) || float.IsInfinity(value) ? name + " is not a finite number" : null;
        }

        static string Numbers(string name, JToken t, int count, string form, float[] values)
        {
            if (!(t is JArray a) || a.Count != count) return name + " is " + form;
            for (int i = 0; i < count; i++)
            {
                string why = Number(name + "[" + i + "]", a[i], out values[i]);
                if (why != null) return why;
            }
            return null;
        }

        /// <summary>The room of a floor strip: the middle, the two lengths, the height of a build. What stops it, or null.</summary>
        public static string SetStrip(RagdollRoomValues room, float x0, float x1, float z0, float z1)
        {
            if (!(x1 > x0) || !(z1 > z0)) return "a room runs from x0 to a larger x1 and from z0 to a larger z1";
            room.Center = new Vec2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            room.Size = new Vec3(x1 - x0, BuildRoomHeight, z1 - z0);
            return null;
        }

        static string SetRoom(RagdollRoomValues room, JToken t)
        {
            if (t is JArray)
            {
                var v = new float[4];
                string why = Numbers(Room, t, 4, "[x0, x1, z0, z1]", v);
                return why ?? SetStrip(room, v[0], v[1], v[2], v[3]);
            }
            if (!(t is JObject o)) return "room is [x0, x1, z0, z1] or {center, size}";
            foreach (var m in o.Properties())
            {
                string why = m.Name == "center" ? SetCenter(room, m.Value) : m.Name == "size" ? SetSize(room, m.Value) : "room has no member " + m.Name;
                if (why != null) return why;
            }
            return null;
        }

        static string SetCenter(RagdollRoomValues room, JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
            {
                room.Center = null;
                return null;
            }
            var v = new float[2];
            string why = Numbers(RoomCenter, t, 2, "[x, z] or null", v);
            if (why == null) room.Center = new Vec2(v[0], v[1]);
            return why;
        }

        static string SetSize(RagdollRoomValues room, JToken t)
        {
            var v = new float[3];
            string why = Numbers(RoomSize, t, 3, "[w, h, d]", v);
            if (why == null) room.Size = new Vec3(v[0], v[1], v[2]);
            return why;
        }

        static string SetMode(RagdollFloorValues floor, JToken t)
        {
            string s = t != null && t.Type == JTokenType.String ? t.Value<string>() : null;
            if (!Has(RagdollFloorValues.Modes, s)) return FloorMode + " is one of " + string.Join(", ", RagdollFloorValues.Modes);
            floor.Mode = s;
            return null;
        }

        static string SetFloor(RagdollFloorValues floor, JToken t)
        {
            if (!(t is JObject o)) return "floor is {mode, offset, height}";
            foreach (var m in o.Properties())
            {
                string why = m.Name == "mode" ? SetMode(floor, m.Value)
                    : m.Name == "offset" ? Number(FloorOffset, m.Value, out floor.Offset)
                    : m.Name == "height" ? Number(FloorHeight, m.Value, out floor.Height)
                    : "floor has no member " + m.Name;
                if (why != null) return why;
            }
            return null;
        }
    }
}
