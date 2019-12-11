using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace Platform
{
    public class Program : MyGridProgram
    {
        public void Main(string argument)
        {
            // Protected
            if (!PrinterGPSUtils.Initialized)
            {
                // Initialize class
                if (PrinterGPSUtils.Initialize(GridTerminalSystem, "Marker A", "Marker B"))
                    Echo("GPSUtils setup complete.");
                else
                {
                    Echo("GPSUtils setup failed!");
                    Echo("Could not find the printer markers.");
                    return;
                }
            }
            // Protected
            if(argument == "getPath")
            {
                double offset = 10;
                List<MyGPSWaypoint> waypoints = PrinterGPSUtils.GetPath(3, offset);
                StringBuilder builder = new StringBuilder();
                foreach(MyGPSWaypoint waypoint in waypoints)
                {
                    builder.AppendLine(waypoint.Serialize());
                }

                Me.CustomData = builder.ToString();
                Echo($"Plotted a path containing {waypoints.Count} data points.");
            }
        }
    }

    public class MyGPSWaypoint
    {
        public double X;
        public double Y;
        public double Z;
        public string Name;
        public bool IsEmpty;
        
        public string Serialize()
        {
            return $"GPS:{Name}:{X}:{Y}:{Z}:";
        }

        public Vector3D ToVector()
        {
            return new Vector3D(X, Y, Z);
        }

        public static MyGPSWaypoint Deserialize(string serializedData)
        {
            MyGPSWaypoint waypoint = new MyGPSWaypoint();
            string[] chunks = serializedData.Split(':');
            if (chunks.Length != 5) return null;
            if (chunks[0] != "GPS") return null;
            waypoint.Name = chunks[1];
            if (!double.TryParse(chunks[2], out waypoint.X)) return null;
            if (!double.TryParse(chunks[3], out waypoint.Y)) return null;
            if (!double.TryParse(chunks[4], out waypoint.Z)) return null;
            return waypoint;
        }

        public MyGPSWaypoint(string serializedData)
        {
            MyGPSWaypoint _this = MyGPSWaypoint.Deserialize(serializedData);
            if (_this == null) return;
            X = _this.X;
            Y = _this.Y;
            Z = _this.Z;
            Name = _this.Name;
            IsEmpty = false;
        }

        public MyGPSWaypoint(double x, double y, double z, string name)
        {
            X = x;
            Y = y;
            Z = z;
            Name = name;
            IsEmpty = false;
        }

        public MyGPSWaypoint()
        {
            IsEmpty = true;
        }

        public MyGPSWaypoint(string name, Vector3 vector)
        {
            X = vector.X;
            Y = vector.Y;
            Z = vector.Z;
            Name = name;
        }

        public static implicit operator MyWaypointInfo(MyGPSWaypoint w)
        {
            return new MyWaypointInfo(w.Name, w.X, w.Y, w.Z);
        }
    }
    public static class PrinterController
    {
        public static IMyShipConnector ConnectorFrontA;
        public static IMyShipConnector ConnectorFrontB;
        public static IMyShipConnector ConnectorBackA;
        public static IMyShipConnector ConnectorBackB;
        public static IMyRemoteControl RemoteControl;
        private static IMyGridTerminalSystem GridTerminalSystem;

        public static List<MyGPSWaypoint> CurrentWaypoints;

        public static bool IsPrinterNull()
        {
            return (ConnectorFrontA == null || ConnectorFrontB == null || ConnectorBackA == null || ConnectorBackB == null || RemoteControl == null);
        }

        public static void CancelTask()
        {
            RemoteControl.ClearWaypoints();
            RemoteControl.SetAutoPilotEnabled(false);
            CurrentWaypoints.Clear();
        }

        public static bool IsWorking()
        {
            return (RemoteControl.IsAutoPilotEnabled);
        }

        public static bool SetTask(List<MyGPSWaypoint> dataPoints, Base6Directions.Direction direction, bool overrideCurrentTask = true)
        {
            if (IsWorking() && overrideCurrentTask) CancelTask();
            else if (IsWorking()) return false;

            foreach (MyGPSWaypoint waypoint in dataPoints)
                RemoteControl.AddWaypoint(waypoint);

            RemoteControl.Direction = direction;
            RemoteControl.SetDockingMode(true);
            RemoteControl.SetAutoPilotEnabled(true);
            return true;
        }

        public static bool Initialize(IMyGridTerminalSystem GTS, string connectorFrontA_name, string connectorFrontB_name, string connectorBackA_name, string connectorBackB_name, string remoteControl_name)
        {
            try
            {
                if (GTS == null) return false;
                GridTerminalSystem = GTS;
                ConnectorFrontA = GridTerminalSystem.GetBlockWithName(connectorFrontA_name) as IMyShipConnector;
                ConnectorFrontB = GridTerminalSystem.GetBlockWithName(connectorFrontB_name) as IMyShipConnector;
                ConnectorBackA = GridTerminalSystem.GetBlockWithName(connectorBackA_name) as IMyShipConnector;
                ConnectorBackB = GridTerminalSystem.GetBlockWithName(connectorBackB_name) as IMyShipConnector;
                RemoteControl = GridTerminalSystem.GetBlockWithName(remoteControl_name) as IMyRemoteControl;
                if (ConnectorFrontA == null || ConnectorFrontB == null || ConnectorBackA == null
                    || ConnectorBackB == null || RemoteControl == null) return false;
            }
            catch(Exception)
            {
                return false;
            }
            return true;
        }

        public static bool Initialize(IMyGridTerminalSystem GTS, IMyShipConnector connectorFrontA, IMyShipConnector connectorFrontB, IMyShipConnector connectorBackA, IMyShipConnector connectorBackB, IMyRemoteControl remoteControl)
        {
            try
            {
                if (GTS == null) return false;
                GridTerminalSystem = GTS;
                ConnectorFrontA = connectorFrontA;
                ConnectorFrontB = connectorFrontB;
                ConnectorBackA = connectorBackA;
                ConnectorBackB = connectorBackB;
                RemoteControl = remoteControl;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
    public static class PrinterGPSUtils
    {
        public static IMyGridTerminalSystem GridTerminalSystem;
        public static IMyTerminalBlock MarkerA;
        public static IMyTerminalBlock MarkerB;
        public static bool Initialized;

        public static double GetPrinterLength()
        {
            if (MarkersNull()) return -1;
            return Vector3.Distance(MarkerA.GetPosition(), MarkerB.GetPosition());
        }

        public static Vector3D GetPrinterDirection()
        {
            return Vector3.Normalize(MarkerB.GetPosition() - MarkerA.GetPosition());
        }

        public static Vector3D GetPrinterDirectionInverse()
        {
            return Vector3.Normalize(MarkerA.GetPosition() - MarkerB.GetPosition());
        }

        public static List<MyGPSWaypoint> GetPath(int alignmentPoints, double forward_offset)
        {
            if (MarkersNull()) return null;
            Vector3D direction = GetPrinterDirection();
            Vector3D origin = MarkerA.GetPosition() + (direction * forward_offset);
            Vector3D endpoint = MarkerB.GetPosition() + (GetPrinterDirectionInverse() * forward_offset);
            double distance_between_points = Vector3.Distance(origin, endpoint) / alignmentPoints;
            List<MyGPSWaypoint> waypoints = new List<MyGPSWaypoint>();
            for (int i = 0; i < alignmentPoints + 1; i++)
                waypoints.Add(new MyGPSWaypoint((i + 1).ToString(), origin + ((direction * distance_between_points) * (i))));
            return waypoints;
        }

        public static bool MarkersNull()
        {
            return (GridTerminalSystem == null || MarkerA == null || MarkerB == null);
        }

        public static bool Initialize(IMyGridTerminalSystem GTS, string markerA_name, string markerB_name)
        {
            try
            {
                if (GTS == null) return false;
                GridTerminalSystem = GTS;
                IMyTerminalBlock markerA = GridTerminalSystem.GetBlockWithName(markerA_name);
                IMyTerminalBlock markerB = GridTerminalSystem.GetBlockWithName(markerB_name);
                if (markerA == null) return false;
                if (markerB == null) return false;
                MarkerA = markerA;
                MarkerB = markerB;
            }
            catch (Exception)
            {
                return false;
            }
            Initialized = true;
            return true;
        }

        public static bool Initialize(IMyGridTerminalSystem GTS, IMyTerminalBlock markerA, IMyTerminalBlock markerB)
        {
            try
            {
                if (GTS == null) return false;
                GridTerminalSystem = GTS;
                if (markerA == null) return false;
                if (markerB == null) return false;
                MarkerA = markerA;
                MarkerB = markerB;
            }
            catch (Exception)
            {
                return false;
            }
            Initialized = true;
            return true;
        }
    }
}
