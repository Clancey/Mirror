using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Mirror.Tests.DeltaCompression
{
    // inventory is interesting. mostly ints.
    public struct InventorySlot
    {
        public int itemId;
        public int amount;
    }

    // skills are interesting. ushorts, doubles, etc. are all != 4 byte ints.
    public struct SkillSlot
    {
        public ushort skillId;
        public double cooldown;
    }

    // test monster for compression.
    // can't be in separate file because Unity complains about it being an Editor
    // script because it's in the Editor folder.
    public class CompressionMonster : NetworkBehaviour
    {
        // a variable length name field
        [SyncVar] public string monsterName;

        // a couple of fixed length fields
        [SyncVar] public int health;
        [SyncVar] public int mana;
        // something != 4 byte inbetween
        [SyncVar] public byte level;
        [SyncVar] public Vector3 position;
        [SyncVar] public Quaternion rotation;

        // variable length inventory
        SyncList<InventorySlot> inventory = new SyncList<InventorySlot>();

        // a couple more fixed fields AFTER variable length inventory
        // to make sure they are still delta compressed decently.
        [SyncVar] public int strength;
        [SyncVar] public int intelligence;
        [SyncVar] public int damage;
        [SyncVar] public int defense;

        // variable length skills
        SyncList<SkillSlot> skills = new SyncList<SkillSlot>();

        public void Initialize(
            string monsterName,
            int health, int mana,
            byte level,
            Vector3 position, Quaternion rotation,
            List<InventorySlot> inventory,
            int strength, int intelligence,
            int damage, int defense,
            List<SkillSlot> skills)
        {
            this.monsterName = monsterName;
            this.health = health;
            this.mana = mana;
            this.level = level;
            this.position = position;
            this.rotation = rotation;

            foreach (InventorySlot slot in inventory)
                this.inventory.Add(slot);

            this.strength = strength;
            this.intelligence = intelligence;
            this.damage = damage;
            this.defense = defense;

            foreach (SkillSlot slot in skills)
                this.skills.Add(slot);
        }
    }

    // all compression approaches should inherit to compare them
    public abstract class DeltaCompressionTests
    {
        // two snapshots
        protected CompressionMonster original;
        protected CompressionMonster tinychange;
        protected CompressionMonster smallchange;
        protected CompressionMonster bigchange;

        // the algorithm to use
        public abstract void ComputeDelta(NetworkWriter from, NetworkWriter to, NetworkWriter result);
        public abstract void ApplyPatch(NetworkWriter from, NetworkReader delta, NetworkWriter result);

        protected void CreateOriginal()
        {
            // create the monster with unique values
            original = new GameObject().AddComponent<CompressionMonster>();
            original.Initialize(
                // name, health, mana, level
                "Skeleton",
                100,
                200,
                60,
                // position, rotation
                new Vector3(10, 20, 30),
                Quaternion.identity,
                // inventory
                new List<InventorySlot>{
                    new InventorySlot{amount=0, itemId=0},
                    new InventorySlot{amount=1, itemId=42},
                    new InventorySlot{amount=50, itemId=43},
                    new InventorySlot{amount=0, itemId=0}
                },
                // strength, intelligence, damage, defense
                10,
                11,
                1000,
                500,
                // skills
                new List<SkillSlot>{
                    new SkillSlot{skillId=4, cooldown=0},
                    new SkillSlot{skillId=8, cooldown=1},
                    new SkillSlot{skillId=16, cooldown=2.5},
                    new SkillSlot{skillId=23, cooldown=60}
                }
            );
        }

        // tiny: only monster.health (4 bytes) changed
        protected void CreateTinyChange()
        {
            tinychange = new GameObject().AddComponent<CompressionMonster>();
            tinychange.Initialize(
                // name, health, mana, level
                "Skeleton",
                99,
                200,
                60,
                // position, rotation
                new Vector3(10, 20, 30),
                Quaternion.identity,
                // inventory
                new List<InventorySlot>{
                    new InventorySlot{amount=0, itemId=0},
                    new InventorySlot{amount=1, itemId=42},
                    new InventorySlot{amount=50, itemId=43},
                    new InventorySlot{amount=0, itemId=0}
                },
                // strength, intelligence, damage, defense
                10,
                11,
                1000,
                500,
                // skills
                new List<SkillSlot>{
                    new SkillSlot{skillId=4, cooldown=0},
                    new SkillSlot{skillId=8, cooldown=1},
                    new SkillSlot{skillId=16, cooldown=2.5},
                    new SkillSlot{skillId=23, cooldown=60}
                }
            );
        }

        // small: health, mana, position.x, 1 item amount, 2 skill cds (32 bytes)
        protected void CreateSmallChange()
        {
            // change it a little
            smallchange = new GameObject().AddComponent<CompressionMonster>();
            smallchange.Initialize(
                // name, health, mana, level
                "Skeleton",
                95,
                180,
                60,
                // position, rotation
                new Vector3(9, 20, 30),
                Quaternion.identity,
                // inventory
                new List<InventorySlot>{
                    new InventorySlot{amount=0, itemId=0},
                    new InventorySlot{amount=1, itemId=42},
                    new InventorySlot{amount=49, itemId=43},
                    new InventorySlot{amount=0, itemId=0}
                },
                // strength, intelligence, damage, defense
                10,
                11,
                1000,
                500,
                // skills
                new List<SkillSlot>{
                    new SkillSlot{skillId=4, cooldown=0},
                    new SkillSlot{skillId=8, cooldown=0.5},
                    new SkillSlot{skillId=16, cooldown=0},
                    new SkillSlot{skillId=23, cooldown=60}
                }
            );
        }

        protected void CreateBigChange()
        {
            // change it a lot
            bigchange = new GameObject().AddComponent<CompressionMonster>();
            bigchange.Initialize(
                // name, health, mana, level
                "Skeleton (Dead)",
                0,
                99,
                61,
                // position, rotation
                new Vector3(11, 22, 30),
                Quaternion.identity,
                // inventory
                new List<InventorySlot>{
                    new InventorySlot{amount=5, itemId=42},
                    new InventorySlot{amount=6, itemId=43},
                },
                // strength, intelligence, damage, defense
                12,
                13,
                5000,
                2000,
                // skills: assume two were buffs and are now gone
                new List<SkillSlot>{
                    new SkillSlot{skillId=16, cooldown=0},
                    new SkillSlot{skillId=23, cooldown=25}
                }
            );
        }

        [SetUp]
        public void SetUp()
        {
            CreateOriginal();
            CreateTinyChange();
            CreateSmallChange();
            CreateBigChange();
        }

        // helper function for all delta tests
        protected void DeltaTest(NetworkWriter A, NetworkWriter B)
        {
            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(A, B, delta);
            Debug.Log($"A={BitConverter.ToString(A.ToArray())}");
            Debug.Log($"B={BitConverter.ToString(B.ToArray())}");
            Debug.Log($"D={BitConverter.ToString(delta.ToArray())}");
            Debug.Log($"A={A.Position} bytes\nB={B.Position} bytes\nDelta={delta.Position} bytes");
        }

        // tiny: only monster.health (=4 bytes) changed
        [Test]
        public void Delta_TinyChange_4Bytes()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            tinychange.OnSerialize(writerB, true);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // small:
        [Test]
        public void Delta_SmallChange_32Bytes()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            smallchange.OnSerialize(writerB, true);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // run the delta encoding
        [Test]
        public void Delta_BigChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            bigchange.OnSerialize(writerB, true);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // test compression for 1000 bytes with only 1 change
        [Test]
        public void Compression_1000bytes_OneChange()
        {
            // prepare a big byte[]
            byte[] A = new byte[1000];
            byte[] B = new byte[1000];
            // change one value
            B[0] = 0xFF;

            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            NetworkWriter writerB = new NetworkWriter();
            writerA.WriteBytes(A, 0, A.Length);
            writerB.WriteBytes(B, 0, B.Length);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // test compression for 1000 bytes with only 10 changes
        [Test]
        public void Compression_1000bytes_TenChanges()
        {
            // prepare a big byte[]
            byte[] A = new byte[1000];
            byte[] B = new byte[1000];

            // change ten values, equally far apart
            B[0] = 0xFF;
            B[99] = 0xFF;
            B[199] = 0xFF;
            B[299] = 0xFF;
            B[399] = 0xFF;
            B[499] = 0xFF;
            B[599] = 0xFF;
            B[699] = 0xFF;
            B[799] = 0xFF;
            B[899] = 0xFF;
            B[999] = 0xFF;

            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            NetworkWriter writerB = new NetworkWriter();
            writerA.WriteBytes(A, 0, A.Length);
            writerB.WriteBytes(B, 0, B.Length);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // test compression for 1000 bytes with only 10 changes
        // this should be a good test for chunk algorithms
        [Test]
        public void Compression_1000bytes_AreaChanges()
        {
            // prepare a big byte[]
            byte[] A = new byte[1000];
            byte[] B = new byte[1000];

            // change an area in the beginning
            B[1] = 0xFF;
            B[3] = 0xFF;

            // change an area in the middle
            B[500] = 0xFF;
            B[510] = 0xFF;

            // change an area in the end
            B[950] = 0xFF;
            B[960] = 0xFF;

            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            NetworkWriter writerB = new NetworkWriter();
            writerA.WriteBytes(A, 0, A.Length);
            writerB.WriteBytes(B, 0, B.Length);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // test compression for 1000 bytes with insertions.
        // some algorithms use chunks.
        // insertions would offset all chunks.
        // need to see how algorithms behave in those cases.
        [Test]
        public void Compression_1000bytes_AntiChunk()
        {
            // list with unique values
            List<byte> A = new List<byte>();
            for (int i = 0; i < 1000; ++i) A.Add((byte)i);

            // same list but with insertions to offset chunks
            List<byte> B = new List<byte>(A);
            B.Insert(0, 0xFF);
            B.Insert(99, 0xFF);
            B.Insert(199, 0xFF);
            B.Insert(299, 0xFF);
            B.Insert(399, 0xFF);
            B.Insert(499, 0xFF);
            B.Insert(599, 0xFF);
            B.Insert(699, 0xFF);
            B.Insert(799, 0xFF);
            B.Insert(899, 0xFF);
            B.Insert(999, 0xFF);

            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            NetworkWriter writerB = new NetworkWriter();
            writerA.WriteBytes(A.ToArray(), 0, A.Count);
            writerB.WriteBytes(B.ToArray(), 0, B.Count);

            // compute delta
            DeltaTest(writerA, writerB);
        }

        // patch with no changes needs to work too
        [Test]
        public void Patch_Unchanged()
        {
            // test values larger than indices for easier reading
            // -> we want soething like ABCBCDE so we have a reptition of
            //    different values in there like BCBC
            // -> this way we can test what 'insertedB' means
            // -> also want more than one removal at a time (the two '55's)
            byte[] A = {11, 22, 33};
            byte[] B = {11, 22, 33};
            Debug.Log($"A={BitConverter.ToString(A)}");
            Debug.Log($"B={BitConverter.ToString(B)}");
            NetworkWriter writerA = new NetworkWriter();
            writerA.WriteBytes(A, 0, A.Length);
            NetworkWriter writerB = new NetworkWriter();
            writerB.WriteBytes(B, 0, B.Length);

            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(writerA, writerB, delta);

            // apply patch to A to get B
            NetworkWriter patched = new NetworkWriter();
            ApplyPatch(writerA, new NetworkReader(delta.ToArray()), patched);

            // compare
            Debug.Log($"D={BitConverter.ToString(delta.ToArray())}");
            Debug.Log($"P={BitConverter.ToString(patched.ToArray())}");
            Assert.That(patched.ToArray().SequenceEqual(writerB.ToArray()));
        }

        // simply patch test for easy debugging
        [Test]
        public void Patch_SimplifiedExample()
        {
            // test values larger than indices for easier reading
            // -> we want soething like ABCBCDE so we have a reptition of
            //    different values in there like BCBC
            // -> this way we can test what 'insertedB' means
            // -> also want more than one removal at a time (the two '55's)
            byte[] A = {11, 22, 33, 22, 33,         44, 55, 55};
            byte[] B = {11, 22, 33, 22, 33, 22, 33, 44};
            Debug.Log($"A={BitConverter.ToString(A)}");
            Debug.Log($"B={BitConverter.ToString(B)}");
            NetworkWriter writerA = new NetworkWriter();
            writerA.WriteBytes(A, 0, A.Length);
            NetworkWriter writerB = new NetworkWriter();
            writerB.WriteBytes(B, 0, B.Length);

            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(writerA, writerB, delta);

            // apply patch to A to get B
            NetworkWriter patched = new NetworkWriter();
            ApplyPatch(writerA, new NetworkReader(delta.ToArray()), patched);

            // compare
            Debug.Log($"D={BitConverter.ToString(delta.ToArray())}");
            Debug.Log($"P={BitConverter.ToString(patched.ToArray())}");
            Assert.That(patched.ToArray().SequenceEqual(writerB.ToArray()));
        }

        // apply the delta
        [Test]
        public void Patch_TinyChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            tinychange.OnSerialize(writerB, true);

            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(writerA, writerB, delta);

            // apply patch to A to get B
            NetworkWriter patched = new NetworkWriter();
            ApplyPatch(writerA, new NetworkReader(delta.ToArray()), patched);

            // compare
            Debug.Log($"A={BitConverter.ToString(writerA.ToArray())}");
            Debug.Log($"B={BitConverter.ToString(writerB.ToArray())}");
            Debug.Log($"D={BitConverter.ToString(delta.ToArray())}");
            Debug.Log($"P={BitConverter.ToString(patched.ToArray())}");
            Assert.That(patched.ToArray().SequenceEqual(writerB.ToArray()));
        }

        // apply the delta
        [Test]
        public void Patch_SmallChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            smallchange.OnSerialize(writerB, true);

            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(writerA, writerB, delta);

            // apply patch to A to get B
            NetworkWriter patched = new NetworkWriter();
            ApplyPatch(writerA, new NetworkReader(delta.ToArray()), patched);

            // compare
            Debug.Log($"A={BitConverter.ToString(writerA.ToArray())}");
            Debug.Log($"B={BitConverter.ToString(writerB.ToArray())}");
            Debug.Log($"D={BitConverter.ToString(delta.ToArray())}");
            Debug.Log($"P={BitConverter.ToString(patched.ToArray())}");
            Assert.That(patched.ToArray().SequenceEqual(writerB.ToArray()));
        }

        // apply the delta
        [Test]
        public void Patch_BigChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            bigchange.OnSerialize(writerB, true);

            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(writerA, writerB, delta);

            // apply patch to A to get B
            NetworkWriter patched = new NetworkWriter();
            ApplyPatch(writerA, new NetworkReader(delta.ToArray()), patched);

            // compare
            Debug.Log($"A={BitConverter.ToString(writerA.ToArray())}");
            Debug.Log($"B={BitConverter.ToString(writerB.ToArray())}");
            Debug.Log($"D={BitConverter.ToString(delta.ToArray())}");
            Debug.Log($"P={BitConverter.ToString(patched.ToArray())}");
            Assert.That(patched.ToArray().SequenceEqual(writerB.ToArray()));
        }

        // actual benchmark execution can be overwritten for caching etc.
        public virtual void ComputeDeltaBenchmark(NetworkWriter writerA, NetworkWriter writerB, int amount, NetworkWriter result)
        {
            for (int i = 0; i < amount; ++i)
            {
                // reset write each time. don't want to measure resizing.
                result.Position = 0;
                ComputeDelta(writerA, writerB, result);
            }
        }

        // measure performance. needs to be fast enough.
        [Test]
        public void Benchmark_ComputeDelta_10k_TinyChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            tinychange.OnSerialize(writerB, true);

            // compute delta several times
            NetworkWriter result = new NetworkWriter();
            ComputeDeltaBenchmark(writerA, writerB, 10_000, result);
        }

        // measure performance. needs to be fast enough.
        [Test]
        public void Benchmark_ComputeDelta_10k_BigChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            bigchange.OnSerialize(writerB, true);

            // compute delta several times
            NetworkWriter result = new NetworkWriter();
            ComputeDeltaBenchmark(writerA, writerB, 10_000, result);
        }

        // actual benchmark execution can be overwritten for caching etc.
        public virtual void ApplyPatchBenchmark(NetworkWriter writerA, NetworkWriter delta, NetworkWriter result, int amount)
        {
            NetworkReader reader = new NetworkReader(delta.ToArraySegment());
            for (int i = 0; i < amount; ++i)
            {
                // reset write each time. don't want to measure resizing.
                result.Position = 0;
                reader.Position = 0;
                ApplyPatch(writerA, reader, result);
            }
        }

        // measure performance. needs to be fast enough.
        [Test]
        public void Benchmark_ApplyPatch_10k_BigChange()
        {
            // serialize both
            NetworkWriter writerA = new NetworkWriter();
            original.OnSerialize(writerA, true);

            NetworkWriter writerB = new NetworkWriter();
            bigchange.OnSerialize(writerB, true);

            // compute delta
            NetworkWriter delta = new NetworkWriter();
            ComputeDelta(writerA, writerB, delta);

            // apply patch to A to get B
            NetworkWriter patched = new NetworkWriter();
            ApplyPatchBenchmark(writerA, delta, patched, 10_000);
        }
    }
}
