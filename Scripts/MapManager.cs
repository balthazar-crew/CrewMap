
using MagicLeap.Android;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.Meshing;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using Unity.XR.CoreUtils;
using Parabox.Stl;
using System.Text;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using UnityEngine.UI;

public class MapManager : MonoBehaviour
{
    [System.Serializable] public class Marker
    {
        public ulong number;
        public Pose pose;
        public float reprojectionError;
    }
    [System.Serializable] private class MarkerExport
    {
        public Marker[] markers;
    }
    [Serializable] private class MeshQuerySetting
    {
        [SerializeField] public MeshingQuerySettings meshQuerySettings;
        [SerializeField] public float meshDensity;
        [SerializeField] public Vector3 meshBoundsOrigin;
        [SerializeField] public Vector3 meshBoundsRotation;
        [SerializeField] public Vector3 meshBoundsScale;
        [SerializeField] public MeshFilter meshPrefab;
    }

    [SerializeField] private ARMeshManager meshManager;
    [SerializeField] private MeshQuerySetting meshSettings;
    [SerializeField] public string destinationFilePath;
    [SerializeField] private GameObject markerVisualPrefab;

    private Dictionary<ulong, MarkerData> _knownMarkers = new();
    private HashSet<GameObject> _detectorVisuals = new();
    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetectorSettings _markerDetectorSettings;
    private MarkerDetector _currentDetector;
    private StringBuilder _builder = new();
    private MagicLeapMeshingFeature _meshingFeature;
    private int _currentIndex;
    private MeshDetectorFlags[] allFlags;
    private MeshTexturedWireframeAdapter _wireframeAdapter;
    private InputActionAsset _inputActions;
    private InputActionMap _inputMap;
    private XROrigin GetXROrigin() => GetComponentInParentIncludingInactive<XROrigin>();

    private void Awake()
    {
        allFlags = (MeshDetectorFlags[])Enum.GetValues(typeof(MeshDetectorFlags));
    }
    
    IEnumerator Start()
    {
        meshManager.enabled = false;
        yield return new WaitUntil(AreSubsystemsLoaded);
        _meshingFeature = OpenXRSettings.Instance.GetFeature<MagicLeapMeshingFeature>();
        _wireframeAdapter = GetComponent<MeshTexturedWireframeAdapter>();
        if (!_meshingFeature.enabled)
        {
            Debug.LogError($"{nameof(MagicLeapMeshingFeature)} was not enabled. Disabling script");
            enabled = false;
        }
        
        _inputActions = FindObjectOfType<InputActionManager>().actionAssets[0];
        if (_inputActions == null)
            throw new System.NullReferenceException("Could not find an InputActionAsset. Make sure that the MagicLeapInput input actions is present (try reimporting this sample)");

        _inputMap = _inputActions.FindActionMap("Controller");
        _inputMap.FindAction("Trigger").performed += SaveMesh;

        Permissions.RequestPermission(Permissions.SpatialMapping, OnPermissionGranted, OnPermissionDenied);
        
        _markerFeature = OpenXRSettings.Instance.GetFeature<MagicLeapMarkerUnderstandingFeature>();
        _detectorVisuals = new HashSet<GameObject>();
        CreateDetector();
    }
 
    private void OnDestroy()
    {
        _inputMap.FindAction("Trigger").performed -= SaveMesh;
        ClearVisuals();
        _markerFeature.DestroyAllMarkerDetectors();
    }

    private void Update()
    {
        _builder.Clear();
        _markerFeature.UpdateMarkerDetectors();
        _currentDetector = _markerFeature.MarkerDetectors[0];
        
        UpdateMarkers(_currentDetector.Data);
    }
    
    private void CreateDetector()
    {
        _markerDetectorSettings.MarkerDetectorProfile = MarkerDetectorProfile.Default;
        _markerDetectorSettings.MarkerType = MarkerType.Aruco;
        _markerDetectorSettings.ArucoSettings.EstimateArucoLength = true;
        _markerFeature.CreateMarkerDetector(_markerDetectorSettings);
    }

    private void ClearVisuals()
    {
        foreach (var visual in _detectorVisuals)
            Destroy(visual.gameObject);
        _detectorVisuals.Clear();
    }
    
    private void UpdateMarkers(IReadOnlyList<MarkerData> markers)
    {
        bool refresh = false;
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].MarkerPose == null || markers[i].MarkerPose.Value.position == Vector3.zero || markers[i].MarkerNumber.HasValue == false)
                continue;
            
            refresh = true;
            
            MarkerData m = new MarkerData();
            
            m.MarkerPose = markers[i].MarkerPose;
            m.MarkerNumber = markers[i].MarkerNumber;
            m.ReprojectionErrorMeters = markers[i].ReprojectionErrorMeters;

            if (_knownMarkers.ContainsKey(markers[i].MarkerNumber.Value))
            {
                if (_knownMarkers[markers[i].MarkerNumber.Value].ReprojectionErrorMeters < m.ReprojectionErrorMeters 
                    && Vector3.Distance(_knownMarkers[markers[i].MarkerNumber.Value].MarkerPose.Value.position,  m.MarkerPose.Value.position) < 0.01f)
                    _knownMarkers[markers[i].MarkerNumber.Value] = m;
            }
            else
                _knownMarkers.Add(markers[i].MarkerNumber.Value, m);
        }

        if (refresh)
        {
            ClearVisuals();
            foreach (var m in _knownMarkers.Values)
            {
                GameObject markerVisual = Instantiate(markerVisualPrefab);
                _detectorVisuals.Add(markerVisual);
                markerVisual.transform.position = m.MarkerPose.Value.position;
                markerVisual.transform.rotation =  m.MarkerPose.Value.rotation;
                _builder.AppendLine("Marker:" + m.MarkerNumber.Value);
                _builder.AppendLine("Error: " + m.ReprojectionErrorMeters);
                
                markerVisual.GetComponentInChildren<Text>().text = _builder.ToString();
            }
        }
    }

    private string ExportMakrers()
    {
        MarkerExport export = new MarkerExport();
        List<Marker> ml = new List<Marker>();
        foreach (var m in _knownMarkers.Values)
        {
            Marker marker = new Marker();
            if (m.MarkerNumber != null) marker.number = m.MarkerNumber.Value;
            if (m.MarkerPose != null) marker.pose = m.MarkerPose.Value;
            marker.reprojectionError = m.ReprojectionErrorMeters;
            ml.Add(marker);
        }
        export.markers = ml.ToArray();
        return JsonUtility.ToJson(export);
    }
    
    T GetComponentInParentIncludingInactive<T>() where T : Component
    {
        var parent = transform.parent;
        while (parent)
        {
            var component = parent.GetComponent<T>();
            if (component)
                return component;

            parent = parent.parent;
        }

        return null;
    }
    
    private void SaveMesh(InputAction.CallbackContext callbackContext)
    {
        meshManager.enabled = false;
        
        string path = Application.persistentDataPath + "/" +destinationFilePath;
        
        XROrigin origin = GetXROrigin(); 
        
        MeshFilter[] objects = origin.TrackablesParent.GetComponentsInChildren<MeshFilter>();
        
        List<GameObject> gos = new List<GameObject>();
        
        Debug.Log("crcrew exportiong to " + path);
        foreach (var ob in objects)
        {
            Debug.Log("crcrew obj :" + ob.name);
            ob.sharedMesh.RecalculateNormals();
            gos.Add(ob.gameObject);
        }
        
        Exporter.Export(path + ".stl", gos.ToArray(), FileType.Binary);
        
        File.WriteAllText(path + ".json", ExportMakrers());
        
        meshManager.enabled = true;
    }

    private void UpdateSettings()
    {
        _meshingFeature.MeshRenderMode = MeshingMode.Triangles;
        meshManager.transform.localScale = meshSettings.meshBoundsScale;
        meshManager.transform.rotation = Quaternion.Euler(meshSettings.meshBoundsRotation);
        meshManager.transform.localPosition = meshSettings.meshBoundsOrigin;
        
        meshManager.density = meshSettings.meshDensity;
        meshManager.meshPrefab = meshSettings.meshPrefab;

        if (_wireframeAdapter != null)
        {
            _wireframeAdapter.ComputeConfidences = meshSettings.meshQuerySettings.meshDetectorFlags.HasFlag(MeshDetectorFlags.ComputeConfidence);
            _wireframeAdapter.ComputeNormals = meshSettings.meshQuerySettings.meshDetectorFlags.HasFlag(MeshDetectorFlags.ComputeNormals);
            _wireframeAdapter.enabled = _currentIndex == 0;
        }

        _meshingFeature.UpdateMeshQuerySettings(in meshSettings.meshQuerySettings);
        _meshingFeature.InvalidateMeshes();
        meshManager.DestroyAllMeshes();
        meshManager.enabled = false;
        _meshingFeature.MeshRenderMode = MeshingMode.Triangles;
        meshManager.enabled = true;
    }

    private bool AreSubsystemsLoaded()
    {
        if (XRGeneralSettings.Instance == null) return false;
        if (XRGeneralSettings.Instance.Manager == null) return false;
        var activeLoader = XRGeneralSettings.Instance.Manager.activeLoader;
        if (activeLoader == null) return false;
        return activeLoader.GetLoadedSubsystem<XRMeshSubsystem>() != null;
    }
    
    private void OnPermissionGranted(string permission)
    {
        meshManager.enabled = true;
        UpdateSettings();
    }

    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"Failed to create Meshing Subsystem due to missing or denied {permission} permission. Please add to manifest. Disabling script.");
        enabled = false;
    }
}
