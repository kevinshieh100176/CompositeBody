#if UNITY_ANDROID
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.OpenXR.Features.Android.Tests.Anchors
{
    /// <summary>
    /// Play mode test suite for Android OpenXR anchor functionality.
    /// </summary>
    public class AnchorsPlayModeTestsSuite
    {
        const int k_RepeatCount = 20;

        ARAnchorManager m_AnchorManager;
        Dictionary<TrackableId, ARAnchor> m_PresentTrackableIdsToAnchors = new();
        static CancellationTokenSource s_Cts;

        /// <summary>
        /// Value source for test repetitions
        /// </summary>
        public static IEnumerable<int> RepeatIndices
        {
            get
            {
                for (int i = 0; i < k_RepeatCount; ++i)
                {
                    yield return i;
                }
            }
        }

        /// <summary>
        /// Sets up the test environment by loading the test scene and erasing all anchors.
        /// </summary>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnitySetUp]
        public IEnumerator Setup()
        {
            Debug.Log("[AnchorProvider] TEST SETUP");

            SceneManager.LoadScene("Packages/com.unity.xr.androidxr-openxr/Tests/Runtime/Anchors/Scene/AnchorsRuntimeTests");
            yield return null;

            m_AnchorManager = Object.FindAnyObjectByType<ARAnchorManager>();

            while (ARSession.state != ARSessionState.SessionTracking)
            {
                yield return null;
            }

            Assert.NotNull(m_AnchorManager, "ARAnchorManager not found in scene.");
            Assert.NotNull(m_AnchorManager.subsystem, "XRAnchorSubsystem is null.");
            Assert.IsTrue(m_AnchorManager.subsystem.running, "XRAnchorSubsystem is not running");
            Assert.IsInstanceOf<AndroidOpenXRAnchorSubsystem>(m_AnchorManager.subsystem, "XRAnchorSubsystem is not AndroidOpenXRAnchorSubsystem");
            s_Cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Cleans up after tests by erasing all anchors.
        /// </summary>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTearDown]
        public IEnumerator OnComplete()
        {
            s_Cts?.Cancel();

            foreach (var kvp in m_PresentTrackableIdsToAnchors)
            {
                if (kvp.Value != null)
                {
                    m_AnchorManager.TryRemoveAnchor(kvp.Value);
                }
            }

            m_PresentTrackableIdsToAnchors.Clear();

            yield return RunAsync(async () =>
            {
                // If the anchors was erased and TryGetSavedAnchorIdsAsync called too quickly after, then Gooogle's runtime sometimes are going to return invalid guids
                // , when TryGetSavedAnchorIdsAsync is called. Possibly a bug on Google's side.
                await DelayFrames(1);
                await EraseAllAnchors();
            });

            Debug.Log("[AnchorProvider] TEST TEARDOWN");
        }

        async Awaitable EraseAllAnchors()
        {
            var getSavedIdsResult = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (getSavedIdsResult.value)
            {
                var eraseResults = new List<XREraseAnchorResult>();
                await m_AnchorManager.TryEraseAnchorsAsync(getSavedIdsResult.value, eraseResults);
            }
        }

        static async Awaitable DelayFrames(int numFrames)
        {
            for (int i = 0; i < numFrames; i++)
            {
                await Awaitable.EndOfFrameAsync();
            }
        }

        /// <summary>
        /// Creates an anchor at a random pose.
        /// </summary>
        async Awaitable<ARAnchor> TryCreateAnchor()
        {
            var pose = new Pose(new Vector3(Random.Range(-5f, 5f), Random.Range(0f, 5f), Random.Range(0f, 5f)), Quaternion.identity);
            var result = await m_AnchorManager.TryAddAnchorAsync(pose);
            Assert.IsTrue(result.status.IsSuccess(), $"Anchor was not created, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");
            Assert.IsNotNull(result.value, $"Anchor is null, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");
            Assert.AreNotEqual(TrackableId.invalidId, result.value.trackableId, "Trackable id of the created anchor was invalid.");
            m_PresentTrackableIdsToAnchors[result.value.trackableId] = result.value;

            return result.value;
        }

        /// <summary>
        /// Creates an anchor at a given pose.
        /// </summary>
        async Awaitable<ARAnchor> TryCreateAnchor(Pose pose)
        {
            var result = await m_AnchorManager.TryAddAnchorAsync(pose);

            Assert.IsTrue(result.status.IsSuccess(), $"Anchor was not created, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");
            Assert.AreNotEqual(result.value.trackableId, TrackableId.invalidId, "Trackable id of the created anchor was invalid.");
            Assert.IsNotNull(result.value, $"Anchor is null, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");
            m_PresentTrackableIdsToAnchors[result.value.trackableId] = result.value;

            return result.value;
        }

        async Awaitable<SerializableGuid> TrySaveAnchor(ARAnchor anchor)
        {
            var result = await m_AnchorManager.TrySaveAnchorAsync(anchor);
            Assert.IsTrue(result.status.IsSuccess(), $"Anchor was not saved. StatusCode: {result.status.statusCode}, nativeStatusCode: {result.status.nativeStatusCode}");
            Assert.AreNotEqual(SerializableGuid.empty, result.value, $"Saved guid is invalid, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");
            Assert.AreNotEqual(TrackableId.invalidId, result.value, $"Saved guid is invalid, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");

            return result.value;
        }

        async Awaitable<bool> TryRemoveAnchor(ARAnchor anchor)
        {
            Assert.NotNull(anchor, "Anchor is null.");
            var id = anchor.trackableId;
            var wasRemoved = m_AnchorManager.TryRemoveAnchor(anchor);
            Assert.IsTrue(wasRemoved, "Anchor was not removed.");
            m_PresentTrackableIdsToAnchors.Remove(id);
            await DelayFrames(1);

            return true;
        }

        async Awaitable<XRResultStatus> TryEraseAnchor(SerializableGuid guid)
        {
            var result = await m_AnchorManager.TryEraseAnchorAsync(guid);
            Assert.IsTrue(result.IsSuccess(), $"Erasing anchor failed, nativeStatusCode: {result.nativeStatusCode}, statusCode: {result.statusCode}.");
            await DelayFrames(1);

            return result;
        }

        async Awaitable TryEraseAnchors(IList<SerializableGuid> savedAnchorGuidsToErase,
            List<XREraseAnchorResult> eraseAnchorResults)
        {
            await m_AnchorManager.TryEraseAnchorsAsync(savedAnchorGuidsToErase, eraseAnchorResults);
            Assert.AreEqual(savedAnchorGuidsToErase.Count, eraseAnchorResults.Count, "Missmatched length of anchors to erase and erased anchors lists");

            foreach (var eraseAnchorResult in eraseAnchorResults)
            {
                Assert.IsTrue(savedAnchorGuidsToErase.Contains(eraseAnchorResult.savedAnchorGuid),
                    $"Erased anchor id {eraseAnchorResult.savedAnchorGuid} was not in the list of anchors to erase");
                Assert.IsTrue(eraseAnchorResult.resultStatus.IsSuccess(),
                    $"Erasing anchor failed, nativeStatusCode: {eraseAnchorResult.resultStatus.nativeStatusCode}, statusCode: {eraseAnchorResult.resultStatus.statusCode}.");
            }

            await DelayFrames(3);
            // it must be a bug in Android runtime. It returns that the anchor was erased, but if try re-saving it right after
            // it would fail with "anchor with this id already existing"
        }

        async Awaitable<ARAnchor> TryLoadAnchor(SerializableGuid guid)
        {
            var result = await m_AnchorManager.TryLoadAnchorAsync(guid);
            Assert.IsTrue(result.status.IsSuccess(), $"Loading anchor failed, nativeStatusCode: {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}.");
            Assert.IsNotNull(result.value, "Loaded anchor is null.");
            var anchor = result.value;
            Assert.AreNotEqual(TrackableId.invalidId, anchor.trackableId, "Trackable id is invalid.");
            m_PresentTrackableIdsToAnchors[anchor.trackableId] = result.value;
            await DelayFrames(1);

            return result.value;
        }

        /// <summary>
        /// Creates and saves the specified number of anchors.
        /// </summary>
        async Awaitable<List<SerializableGuid>> CreateSaveAndThenRemoveAnchors(int num)
        {
            var savedAnchorGuids = new List<SerializableGuid>(num);
            var createdAnchors = new List<ARAnchor>(num);

            for (int i = 0; i < num; i++)
            {
                var anchor = await TryCreateAnchor();
                createdAnchors.Add(anchor);
                var persistedId = await TrySaveAnchor(anchor);

                savedAnchorGuids.Add(persistedId);
            }

            Assert.AreEqual(num, savedAnchorGuids.Count, "Missmatched number of saved anchors and asked.");
            Assert.AreEqual(num, createdAnchors.Count, "Missmatched number of created anchors and asked.");

            foreach (var anchor in createdAnchors)
            {
                await TryRemoveAnchor(anchor);
            }

            return savedAnchorGuids;
        }

        /// <summary>
        /// Creates the specified number of anchors.
        /// </summary>
        async Awaitable<List<ARAnchor>> CreateAnchors(int num)
        {
            var anchors = new List<ARAnchor>(num);

            for (int i = 0; i < num; i++)
            {
                var anchor = await TryCreateAnchor();
                anchors.Add(anchor);
            }

            return anchors;
        }

        /// <summary>
        /// Tests that loading after erasing an anchor fails.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator LoadAfterErase_ShouldFail([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][LoadAfterErase_ShouldFail] Run {run}");
            var savedGuids = await CreateSaveAndThenRemoveAnchors(1);
            await TryEraseAnchor(savedGuids[0]);

            var loadResult = await m_AnchorManager.TryLoadAnchorAsync(savedGuids[0]);
            Assert.IsFalse(loadResult.status.IsSuccess(), $"Loading anchor {savedGuids[0]} after deletion did not return an error.");
        });

        /// <summary>
        /// Tests that getting saved anchor IDs when empty returns an empty list.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator GetSavedAnchorIds_WhenEmpty_ShouldReturnEmptyList([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][GetSavedAnchorIds_WhenEmpty_ShouldReturnEmptyList] Run {run}");
            var result = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (result.value)
            {
                Assert.IsTrue(result.status.IsSuccess(), $"{result.status.nativeStatusCode}.");
                Assert.AreEqual(0, result.value.Length, "Expected no saved anchors.");
            }
        });

        /// <summary>
        /// Tests that batch erase with an invalid GUID succeeds but returns an error for the invalid GUID.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator BatchErase_WithInvalidGuid_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][BatchErase_WithInvalidGuid_ShouldSucceed] Run {run}");
            var savedGuids = await CreateSaveAndThenRemoveAnchors(2);
            savedGuids.Add(new SerializableGuid(Guid.NewGuid())); // invalid

            var eraseResults = new List<XRResultStatus>();

            foreach (var savedGuid in savedGuids)
            {
                var result = await m_AnchorManager.TryEraseAnchorAsync(savedGuid);
                eraseResults.Add(result);
            }

            Assert.AreEqual(savedGuids.Count, eraseResults.Count, "Mismatch in erase results count.");
            Assert.IsTrue(eraseResults.Exists(r => r.IsError()), "Expected at least one error for invalid GUID.");
        });

        /// <summary>
        /// Tests that multiple anchors at the same pose are distinct.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator MultipleAnchorsSamePose_ShouldBeDistinct([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][MultipleAnchorsSamePose_ShouldBeDistinct] Run {run}");
            var pose = new Pose(Vector3.forward, Quaternion.identity);

            var anchorA = await TryCreateAnchor(pose);
            var anchorB = await TryCreateAnchor(pose);

            Assert.AreNotEqual(anchorA.trackableId, anchorB.trackableId, "Anchors at same pose have identical IDs.");
        });

        /// <summary>
        /// Tests that getting saved anchor IDs returns a list.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator GetSavedAnchorIds_ShouldReturnList([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][GetSavedAnchorIds_ShouldReturnList] Run {run}");
            var result = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (result.value)
            {
                Assert.IsTrue(result.status.IsSuccess(), $"Failed to get saved anchor IDs, {result.status.nativeStatusCode}.");
            }
        });

        /// <summary>
        /// Tests that creating an anchor succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator AnchorCreate_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][AnchorCreate_ShouldSucceed] Run {run}");
            await TryCreateAnchor();
        });

        /// <summary>
        /// Tests that creating and saving an anchor succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveAnchor_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveAnchor_ShouldSucceed] Run {run}");
            var anchor = await TryCreateAnchor();
            Assert.AreNotEqual(anchor.trackableId, TrackableId.invalidId, "Trackable id of the created anchor was invalid.");
            await TrySaveAnchor(anchor);
        });

        /// <summary>
        /// Tests that getting saved IDs succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator GetSavedIds_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][GetSavedIds_ShouldSucceed] Run {run}");
            var savedAnchorGuids = await CreateSaveAndThenRemoveAnchors(3);

            var result = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (result.value)
            {
                Assert.IsTrue(result.status.IsSuccess());

                foreach (var guid in result.value)
                {
                    Assert.AreNotEqual(SerializableGuid.empty, guid, "Guid is invalid.");
                    Assert.IsTrue(savedAnchorGuids.Contains(guid), $"Guid {guid} was not created earlier in the test.");
                }
            }
        });

        /// <summary>
        /// Tests that saved anchors can be loaded one by one.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator GetSavedIdsAndLoadOneByOne_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][GetSavedIdsAndLoadOneByOne_ShouldSucceed] Run {run}");
            var savedAnchorGuids = await CreateSaveAndThenRemoveAnchors(3);
            Assert.IsNotNull(savedAnchorGuids, "Anchors were not created and saved.");

            var loadIdsResult = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (loadIdsResult.value)
            {
                Assert.IsTrue(loadIdsResult.status.IsSuccess(), $"Did not get saved anchor IDs, nativeStatusCode: {loadIdsResult.status.nativeStatusCode}, statusCode: {loadIdsResult.status.statusCode}");

                foreach (var guid in loadIdsResult.value)
                {
                    await TryLoadAnchor(guid);
                }
            }
        });

        /// <summary>
        /// Tests that saved anchors can be loaded in batch.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator GetSavedIdsAndLoadBatch_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][GetSavedIdsAndLoadBatch_ShouldSucceed] Run {run}");
            var savedAnchorGuids = await CreateSaveAndThenRemoveAnchors(3);
            Assert.IsNotNull(savedAnchorGuids, "Anchors were not created and saved.");

            var savedIdsResult = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (savedIdsResult.value)
            {
                Assert.IsTrue(savedIdsResult.status.IsSuccess());
                Assert.AreEqual(3, savedIdsResult.value.Length, "Missmatched length of saved and asked.");

                var loadingResults = new List<ARSaveOrLoadAnchorResult>();
                await m_AnchorManager.TryLoadAnchorsAsync(savedAnchorGuids, loadingResults, null);
                await DelayFrames(1);

                foreach (var anchorLoadingResult in loadingResults)
                {
                    Assert.IsTrue(anchorLoadingResult.resultStatus.IsSuccess(), $"Anchor with persistent id {anchorLoadingResult.savedAnchorGuid.guid} was not loaded.");
                    Assert.IsNotNull(anchorLoadingResult.anchor, "Anchor is null.");
                    Assert.AreNotEqual(TrackableId.invalidId, anchorLoadingResult.anchor.trackableId, $"Anchor with persistent id {anchorLoadingResult.savedAnchorGuid.guid} was loaded with an invalid id.");

                    m_PresentTrackableIdsToAnchors[anchorLoadingResult.anchor.trackableId] = anchorLoadingResult.anchor;
                }
            }
        });

        /// <summary>
        /// Tests that creating, saving, loading, and erasing an anchor succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateRemoveAnchor_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateRemoveAnchor_ShouldSucceed] Run {run}");
            var createdAnchor = await TryCreateAnchor();
            await TryRemoveAnchor(createdAnchor);
        });

        /// <summary>
        /// Tests that creating, saving, loading, and erasing an anchor succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveRemoveLoadEraseAnchor_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveRemoveLoadEraseAnchor_ShouldSucceed] Run {run}");
            var createdAnchor = await TryCreateAnchor();
            var persistedId = await TrySaveAnchor(createdAnchor);
            await TryRemoveAnchor(createdAnchor);
            var loadedAnchor = await TryLoadAnchor(persistedId);
            await TryEraseAnchor(persistedId);
            await TryRemoveAnchor(loadedAnchor);
        });

        /// <summary>
        /// Tests that creating, saving, loading, and erasing an anchor succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveRemoveLoad_PosesShouldBeIdentical([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveRemoveLoad_PosesShouldBeIdentical] Run {run}");
            var createdAnchor = await TryCreateAnchor();
            var persistedId = await TrySaveAnchor(createdAnchor);
            await TryRemoveAnchor(createdAnchor);
            var loadedAnchor = await TryLoadAnchor(persistedId);

            Assert.IsTrue(CompareVectorsApproximately(createdAnchor.pose.position, loadedAnchor.pose.position, 1e-02f), $"Created and loaded anchor pose positions are different: created {createdAnchor.pose.position:F6}, saved and loaded {loadedAnchor.pose.position:F6}");

            Assert.IsTrue(CompareQuaternionsApproximately(createdAnchor.pose.rotation, loadedAnchor.pose.rotation, 1e-02f), $"Created and loaded anchor pose rotations are different: created {createdAnchor.pose.rotation}, saved and loaded {loadedAnchor.pose.rotation}");
        });

        static bool QuaternionHasInf(Quaternion q)
        {
            return float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w);
        }

        static bool QuaternionHasNan(Quaternion q)
        {
            return float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w);
        }

        static bool CompareQuaternionsApproximately(Quaternion q1, Quaternion q2, float epsilon = 1e-6f)
        {
            if (QuaternionHasInf(q1) || QuaternionHasNan(q1) || QuaternionHasInf(q2) || QuaternionHasNan(q2))
                return false;

            var dot = Quaternion.Dot(q1.normalized, q2.normalized);
            return MathF.Abs(dot) >= 1.0f - epsilon;
        }

        static bool CompareVectorsApproximately(Vector3 v1, Vector3 v2, float epsilon = 1e-6f)
        {
            return (v2 - v1).sqrMagnitude < epsilon * epsilon;
        }

        /// <summary>
        /// Tests that creating and saving multiple anchors succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveMultipleAnchors_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveMultipleAnchors_ShouldSucceed] Run {run}");
            var anchors = await CreateAnchors(3);
            Assert.AreEqual(3, anchors.Count, "Missmatched anchors quantity and asked anchors count.");

            var saveAnchorsResults = new List<ARSaveOrLoadAnchorResult>(anchors.Count);
            await m_AnchorManager.TrySaveAnchorsAsync(anchors, saveAnchorsResults);

            foreach (var result in saveAnchorsResults)
            {
                Assert.IsTrue(result.resultStatus.IsSuccess(), $"Anchor guid: {result.savedAnchorGuid}, trackableId: {result.anchor.trackableId}, was not saved");
            }
        });

        /// <summary>
        /// Tests that erasing non-existing anchors fails.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator EraseNonExistingAnchors_ShouldFail([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][EraseNonExistingAnchors_ShouldFail] Run {run}");
            const int anchorsToCreate = 3;
            await CreateSaveAndThenRemoveAnchors(anchorsToCreate);
            var loadIdsResult = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.TempJob);
            using (loadIdsResult.value)
            {
                Assert.IsTrue(loadIdsResult.status.IsSuccess(), $"Could not get persistent anchor ids list, {loadIdsResult.status.nativeStatusCode}");
                Assert.AreEqual(anchorsToCreate, loadIdsResult.value.Length, "Mismatch in loaded ids vs created anchors.");
            }

            var erasingResult = await m_AnchorManager.TryEraseAnchorAsync(new SerializableGuid(Guid.NewGuid()));
            Assert.IsTrue(erasingResult.IsError(), $"Erasing non-existing anchor did not raise an error, {erasingResult.nativeStatusCode}");
        });

        /// <summary>
        /// Tests that erasing all anchors in batch succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator EraseAllAnchorsBatched_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][EraseAllAnchorsBatched_ShouldSucceed] Run {run}");
            const int anchorsToCreate = 3;
            var savedAnchorGuids = await CreateSaveAndThenRemoveAnchors(anchorsToCreate);
            var eraseResults = new List<XREraseAnchorResult>();
            await TryEraseAnchors(savedAnchorGuids, eraseResults);

            Assert.AreEqual(anchorsToCreate, eraseResults.Count, "Mismatch in erased anchors vs created anchors.");

            foreach (var result in eraseResults)
            {
                Assert.IsTrue(result.resultStatus.IsSuccess(), $"Anchor (guid: {result.savedAnchorGuid}) failed to erase. Status: {result.resultStatus.statusCode}, native status code: {result.resultStatus.nativeStatusCode}");
                Assert.IsTrue(savedAnchorGuids.Contains(result.savedAnchorGuid), $"Erased guid: {result.savedAnchorGuid} not found in created anchors list, {result.resultStatus.nativeStatusCode}");
            }
        });

        /// <summary>
        /// Tests that erasing all anchors one by one succeeds.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator EraseAllAnchorsOneByOne_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][EraseAllAnchorsOneByOne_ShouldSucceed] Run {run}");
            const int anchorsToCreate = 3;
            var savedAnchorGuids = await CreateSaveAndThenRemoveAnchors(anchorsToCreate);

            foreach (var guid in savedAnchorGuids)
            {
                await TryEraseAnchor(guid);
            }
        });

        /// <summary>
        /// Tests that loading a non-existing anchor fails.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator LoadNonExistingAnchor_ShouldFail([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][LoadNonExistingAnchor_ShouldFail] Run {run}");
            var fakeGuid = new SerializableGuid(Guid.NewGuid());
            var result = await m_AnchorManager.TryLoadAnchorAsync(fakeGuid);
            Assert.IsTrue(result.status.IsError(), $"Unexpected success loading non-existing anchor, nativeStatusCode {result.status.nativeStatusCode}, statusCode: {result.status.statusCode}");
        });

        /// <summary>
        /// Tests that trying to save a non-existing anchor fails.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateRemoveSaveAnchor_ShouldFail([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateRemoveSaveAnchor_ShouldFail] Run {run}");

            var anchor = await TryCreateAnchor();
            var oldTrackableId = anchor.trackableId;
            await TryRemoveAnchor(anchor);
            var saveResult = await m_AnchorManager.subsystem.TrySaveAnchorAsync(oldTrackableId);
            Assert.IsTrue(saveResult.status.IsError(), "Saving a removed anchor did not fail as expected.");
        });

        /// <summary>
        /// Tests that trying to save a non-existing anchor fails.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveRemoveLoadRemoveSaveAnchor_ShouldFail([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveRemoveLoadRemoveSaveAnchor_ShouldFail] Run {run}");

            var anchor = await TryCreateAnchor();
            var persistedId = await TrySaveAnchor(anchor);
            await TryRemoveAnchor(anchor);
            var loadedAnchor = await TryLoadAnchor(persistedId);
            await TryRemoveAnchor(loadedAnchor);
            var saveResult = await m_AnchorManager.subsystem.TrySaveAnchorAsync(loadedAnchor.trackableId);

            Assert.IsTrue(saveResult.status.IsError(), "Saving a previously saved anchor did not fail as expected.");
        });

        /// <summary>
        /// Tests that loading the same anchor multiple times returns valid anchors each time (if supported).
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator LoadAnchor_MultipleTimes_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][LoadAnchor_MultipleTimes_ShouldSucceed] Run {run}");

            var savedGuids = await CreateSaveAndThenRemoveAnchors(1);
            var guid = savedGuids[0];

            for (int i = 0; i < 3; i++)
            {
                var loadResult = await m_AnchorManager.TryLoadAnchorAsync(guid);
                Assert.IsTrue(loadResult.status.IsSuccess(), $"Failed to load anchor on attempt {i}, nativeStatusCode: {loadResult.status.nativeStatusCode}, statusCode: {loadResult.status.statusCode}");
                Assert.IsNotNull(loadResult.value, $"Loaded anchor is null on attempt {i}.");
                m_PresentTrackableIdsToAnchors[loadResult.value.trackableId] = loadResult.value;
            }
        });

        /// <summary>
        /// Tests that removing the same anchor twice is safe and the second attempt fails.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator RemoveAnchor_Twice_ShouldFailSecondTime([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][RemoveAnchor_Twice_ShouldFailSecondTime] Run {run}");

            var anchor = await TryCreateAnchor();
            var wasRemovedFirst = m_AnchorManager.TryRemoveAnchor(anchor);
            Assert.IsTrue(wasRemovedFirst, "Anchor was not removed the first time.");

            await Awaitable.EndOfFrameAsync();
            Assert.IsNull(anchor, "Anchor is not null.");

            var wasRemovedSecond = m_AnchorManager.subsystem.TryRemoveAnchor(anchor.trackableId);
            Assert.IsFalse(wasRemovedSecond, "Removing the anchor a second time did not fail as expected.");
        });

        /// <summary>
        /// Tests: create anchor, save, remove locally, load from storage, erase from storage, save again.
        /// This sequence should succeed.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveRemoveLoadEraseSaveAgain_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveRemoveLoadEraseSaveAgain_ShouldSucceed] Run {run}");

            var createdAnchor = await TryCreateAnchor();
            var persistedId = await TrySaveAnchor(createdAnchor);
            await TryRemoveAnchor(createdAnchor);
            var loadedAnchor = await TryLoadAnchor(persistedId);
            await TryEraseAnchor(persistedId);
            var newPersistedId = await TrySaveAnchor(loadedAnchor);
            Assert.AreNotEqual(SerializableGuid.empty, newPersistedId, "Resaved anchor guid is invalid.");
        });

        /// <summary>
        /// Tests: create 5 anchors, save, remove locally, load from storage, erase from storage, save again.
        /// This sequence should succeed for all anchors.
        /// </summary>
        /// <param name="run">The current test repetition index.</param>
        /// <returns>Enumerator for coroutine execution.</returns>
        [UnityTest]
        public IEnumerator CreateSaveRemoveLoadEraseSaveAgain_MultipleAnchors_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveRemoveLoadEraseSaveAgain_MultipleAnchors_ShouldSucceed] Run {run}");

            const int anchorsCount = 5;

            var createdAnchors = new List<ARAnchor>(anchorsCount);
            var persistedIds = new List<SerializableGuid>(anchorsCount);

            for (int i = 0; i < anchorsCount; ++i)
            {
                var anchor = await TryCreateAnchor();
                createdAnchors.Add(anchor);
            }

            for (int i = 0; i < anchorsCount; ++i)
            {
                var guid = await TrySaveAnchor(createdAnchors[i]);
                Assert.AreNotEqual(SerializableGuid.empty, guid, $"Saved anchor guid is invalid for anchor {i}.");
                persistedIds.Add(guid);
            }

            for (int i = 0; i < anchorsCount; ++i)
            {
                await TryRemoveAnchor(createdAnchors[i]);
            }

            var loadedAnchors = new List<ARAnchor>(anchorsCount);
            for (int i = 0; i < anchorsCount; ++i)
            {
                var loadedAnchor = await TryLoadAnchor(persistedIds[i]);
                loadedAnchors.Add(loadedAnchor);
            }

            for (int i = 0; i < anchorsCount; ++i)
            {
                await TryEraseAnchor(persistedIds[i]);
            }

            for (int i = 0; i < anchorsCount; ++i)
            {
                var newGuid = await TrySaveAnchor(loadedAnchors[i]);
                Assert.AreNotEqual(SerializableGuid.empty, newGuid, $"Resaved anchor guid is invalid for anchor {i}.");
            }
        });

        [UnityTest]
        public IEnumerator CreateSaveRemoveLoadEraseSaveAgain_MultipleAnchors_Batch_ShouldSucceed([ValueSource(nameof(RepeatIndices))] int run) => RunAsync(async () =>
        {
            Debug.LogWarning($"[AnchorProvider][CreateSaveRemoveLoadEraseSaveAgain_MultipleAnchors_Batch_ShouldSucceed] Run {run}");

            const int anchorsCount = 5;

            var createdAnchors = await CreateAnchors(anchorsCount);

            var saveAnchorsResults = new List<ARSaveOrLoadAnchorResult>(anchorsCount);
            await m_AnchorManager.TrySaveAnchorsAsync(createdAnchors, saveAnchorsResults);

            var persistedIds = saveAnchorsResults.Select(r => r.savedAnchorGuid).ToList();

            foreach (var anchor in createdAnchors)
            {
                await TryRemoveAnchor(anchor);
            }

            var loadAnchorsResults = new List<ARSaveOrLoadAnchorResult>(anchorsCount);
            await m_AnchorManager.TryLoadAnchorsAsync(persistedIds, loadAnchorsResults, null);

            var loadedAnchors = loadAnchorsResults.Select(r => r.anchor).ToList();

            var eraseResults = new List<XREraseAnchorResult>(anchorsCount);
            await TryEraseAnchors(persistedIds, eraseResults);

            var resaveAnchorsResults = new List<ARSaveOrLoadAnchorResult>(anchorsCount);
            await m_AnchorManager.TrySaveAnchorsAsync(loadedAnchors, resaveAnchorsResults);

            foreach (var result in resaveAnchorsResults)
            {
                Assert.IsTrue(result.resultStatus.IsSuccess(), $"Resaving anchor failed, guid: {result.savedAnchorGuid}");
            }
        });


        /// <summary>
        /// Runs an async test as a coroutine.
        /// </summary>
        /// <returns>Enumerator for coroutine execution.</returns>
        private static IEnumerator RunAsync(Func<Awaitable> asyncTest)
        {
            yield return asyncTest();
        }
    }
}
#endif
