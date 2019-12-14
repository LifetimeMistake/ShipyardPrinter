using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace ShipyardManager.Modules.Printer
{
    public class Program : MyGridProgram
    {
        Printer printer;
        Path p;
        public bool Initialize()
        {
            printer = new Printer(GridTerminalSystem, Echo, new PrinterPlatform(GridTerminalSystem, Echo, "Conn Front A", "Conn Front B", "Conn Back A", "Conn Back B", "RC"),
                    new PrinterBase(GridTerminalSystem, Echo, "Conn Origin A", "Conn Origin B", "Conn Endpoint A", "Conn Endpoint B", "Marker Origin", "Marker Endpoint"), 0,
                    2.5, 10, Base6Directions.Direction.Forward);
            if (!printer.IsValid)
            {
                Echo("Failed to initialize the printer instance.");
                return false;
            }
            if (!printer.Platform.IsValid)
            {
                Echo("Failed to initialize the printer platform instance");
                return false;
            }
            if (!printer.Base.IsValid)
            {
                Echo("Failed to initialize the printer base instance");
                return false;
            }

            Echo("Printer instance initialized successfully.");
            return true;
        }
        public void Main(string argument)
        {
            if(printer == null)
                if (!Initialize()) return;
            if (!printer.IsValid)
                if (!Initialize()) return;

            if(argument == "goOrigin")
            {
                Path path = printer.OptimizePath(printer.GetEndpointOriginPath(10, true), printer.Base.GetOriginPos(), true);
                if(path.Waypoints.Count == 0)
                {
                    Echo("Could not plot a path. Task aborted.");
                    return;
                }
                printer.Platform.SetTask(path);
                Echo($"Platform going to Origin. Distance remaining: {Math.Round(Vector3D.Distance(printer.Base.GetOriginPos(), printer.Platform.RemoteControl.GetPosition()), 2)}m");
                Echo($"{Vector3D.Distance(path.Waypoints[0].ToVector(), printer.Platform.RemoteControl.GetPosition())}m remaining to the next data point.");
                return;
            }
            if(argument == "goEndpoint")
            {
                Path path = printer.OptimizePath(printer.GetOriginEndpointPath(10, true), printer.Base.GetEndpointPos(), true);
                if (path.Waypoints.Count == 0)
                {
                    Echo("Could not plot a path. Task aborted.");
                    return;
                }
                printer.Platform.SetTask(path);
                Echo($"Platform going to Endpoint. Distance remaining: {Math.Round(Vector3D.Distance(printer.Base.GetEndpointPos(), printer.Platform.RemoteControl.GetPosition()), 2)}m");
                Echo($"{Vector3D.Distance(path.Waypoints[0].ToVector(), printer.Platform.RemoteControl.GetPosition())}m remaining to the next data point.");
                return;
            }
            if(argument == "shiftPath 1")
            {
                if (p == null) p = printer.GetOriginEndpointPath(10);
                p.ShiftPathRelative(1);
                printer.Platform.SetTask(p);
                return;
            }
            if (argument == "shiftPath -1")
            {
                if (p == null) p = printer.GetOriginEndpointPath(10);
                p.ShiftPathRelative(-1);
                printer.Platform.SetTask(p);
                return;
            }

            Echo("Runtime finished.");
            Echo($"Instruction count: {Runtime.CurrentInstructionCount}");
            Echo($"Last execution time: {Runtime.LastRunTimeMs}ms");
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

        public static implicit operator MyGPSWaypoint(MyWaypointInfo w)
        {
            return new MyGPSWaypoint(w.Name, w.Coords);
        }

        public static implicit operator MyWaypointInfo(MyGPSWaypoint w)
        {
            return new MyWaypointInfo(w.Name, w.X, w.Y, w.Z);
        }
    }
    public class ConnectorPair
    {
        public IMyShipConnector Left;
        public IMyShipConnector Right;

        public bool IsValid;

        public ConnectorPair(IMyShipConnector left, IMyShipConnector right)
        {
            if (left == null) return;
            if (right == null) return;
            Left = left;
            Right = right;
            IsValid = true;
        }

        public void Lock()
        {
            if (!IsValid) return;
            Left.Connect();
            Right.Connect();
        }
        public void Unlock()
        {
            if (!IsValid) return;
            Left.Disconnect();
            Right.Disconnect();
        }
        public void PowerOn()
        {
            if (!IsValid) return;
            Left.ApplyAction("OnOff_On");
            Right.ApplyAction("OnOff_On");
        }
        public void PowerOff()
        {
            if (!IsValid) return;
            Left.ApplyAction("OnOff_Off");
            Right.ApplyAction("OnOff_Off");
        }
    }
    public class PrinterPlatform
    {
        private IMyGridTerminalSystem GTS;
        public ConnectorPair ConnectorsFront;
        public ConnectorPair ConnectorsBack;
        public IMyRemoteControl RemoteControl;
        public Action<string> Echo;
        public List<MyGPSWaypoint> CurrentWaypoints { get { return GetCurrentWaypoints(); } }
        public bool IsValid;

        public PrinterPlatform(IMyGridTerminalSystem gTS, Action<string> echo, ConnectorPair connectorsFront, ConnectorPair connectorsBack, IMyRemoteControl remoteControl)
        {
            if (gTS == null) return;
            if (echo == null) return;
            if (connectorsFront == null) return;
            if (connectorsBack == null) return;
            if (remoteControl == null) return;
            GTS = gTS;
            ConnectorsFront = connectorsFront;
            ConnectorsBack = connectorsBack;
            RemoteControl = remoteControl;
            Echo = echo;

            IsValid = true;
        }
        public PrinterPlatform(IMyGridTerminalSystem gTS, Action<string> echo, string connectorsFront_Left_Name, string connectorsFront_Right_Name, 
            string connectorsBack_Left_Name, string connectorsBack_Right_Name, string remoteControl_Name)
        {
            if (gTS == null) return;
            if (echo == null) return;
            if (connectorsFront_Left_Name == null) return;
            if (connectorsFront_Right_Name == null) return;
            if (connectorsBack_Left_Name == null) return;
            if (connectorsBack_Right_Name == null) return;
            if (remoteControl_Name == null) return;
            GTS = gTS;
            Echo = echo;
            try
            {
                ConnectorsFront = new ConnectorPair((IMyShipConnector)GTS.GetBlockWithName(connectorsFront_Left_Name), (IMyShipConnector)GTS.GetBlockWithName(connectorsFront_Right_Name));
                ConnectorsBack = new ConnectorPair((IMyShipConnector)GTS.GetBlockWithName(connectorsBack_Left_Name), (IMyShipConnector)GTS.GetBlockWithName(connectorsBack_Right_Name));
                RemoteControl = (IMyRemoteControl)GTS.GetBlockWithName(remoteControl_Name);
                if (ConnectorsFront == null) return;
                if (ConnectorsBack == null) return;
                if (RemoteControl == null) return;
            }
            catch(Exception)
            { return; }
            IsValid = true;
        }
        public List<MyGPSWaypoint> GetCurrentWaypoints()
        {
            if (RemoteControl == null) return null;
            List<MyWaypointInfo> waypointInfoList = new List<MyWaypointInfo>();
            RemoteControl.GetWaypointInfo(waypointInfoList);
            List<MyGPSWaypoint> gpsWaypointList = new List<MyGPSWaypoint>();
            waypointInfoList.ForEach((w) => { gpsWaypointList.Add((MyGPSWaypoint)w); });
            return gpsWaypointList;
        }
        public bool SetTask(Path path)
        {
            CancelTask();
            foreach (MyGPSWaypoint waypoint in path.Waypoints)
                RemoteControl.AddWaypoint(waypoint);

            RemoteControl.Direction = path.FlightDirection;
            RemoteControl.SetDockingMode(true);
            RemoteControl.SetAutoPilotEnabled(true);
            return true;
        }
        public void CancelTask()
        {
            if (RemoteControl == null) return;
            if (Echo == null) return;
            Echo((RemoteControl == null).ToString());
            Echo((CurrentWaypoints == null).ToString());
            RemoteControl.ClearWaypoints();
            RemoteControl.SetAutoPilotEnabled(false);
            CurrentWaypoints.Clear();
        }
    }
    public class PrinterBase
    {
        public IMyGridTerminalSystem GTS;
        public IMyTerminalBlock OriginMarker;
        public IMyTerminalBlock EndpointMarker;
        public ConnectorPair ConnectorsOrigin;
        public ConnectorPair ConnectorsEndpoint;
        public Action<string> Echo;
        public bool IsValid;

        public PrinterBase(IMyGridTerminalSystem gTS, IMyTerminalBlock markerOrigin, IMyTerminalBlock markerEndpoint, ConnectorPair connectorsOrigin, ConnectorPair connectorsEndpoint, Action<string> echo)
        {
            if (gTS == null) return;
            if (markerOrigin == null) return;
            if (markerEndpoint == null) return;
            if (connectorsOrigin == null) return;
            if (connectorsEndpoint == null) return;
            if (echo == null) return;
            GTS = gTS;
            OriginMarker = markerOrigin;
            EndpointMarker = markerEndpoint;
            ConnectorsOrigin = connectorsOrigin;
            ConnectorsEndpoint = connectorsEndpoint;
            Echo = echo;
            IsValid = true;
        }
        public PrinterBase(IMyGridTerminalSystem gTS, Action<string> echo, string connectorsOrigin_Left_Name, string connectorsOrigin_Right_Name, string connectorsEndpoint_Left_Name, 
            string connectorsEndpoint_Right_Name, string markerOrigin_Name, string markerEndpoint_Name)
        {
            if (gTS == null) return;
            if (echo == null) return;
            if (connectorsOrigin_Left_Name == null) return;
            if (connectorsOrigin_Right_Name == null) return;
            if (connectorsEndpoint_Left_Name == null) return;
            if (connectorsEndpoint_Right_Name == null) return;
            if (markerOrigin_Name == null) return;
            if (markerEndpoint_Name == null) return;
            GTS = gTS;
            Echo = echo;
            try
            {
                ConnectorsOrigin = new ConnectorPair((IMyShipConnector)GTS.GetBlockWithName(connectorsOrigin_Left_Name), (IMyShipConnector)GTS.GetBlockWithName(connectorsOrigin_Right_Name));
                ConnectorsEndpoint = new ConnectorPair((IMyShipConnector)GTS.GetBlockWithName(connectorsEndpoint_Left_Name), (IMyShipConnector)GTS.GetBlockWithName(connectorsEndpoint_Right_Name));
                OriginMarker = GTS.GetBlockWithName(markerOrigin_Name);
                EndpointMarker = GTS.GetBlockWithName(markerEndpoint_Name);
                if (ConnectorsOrigin == null) return;
                if (ConnectorsEndpoint == null) return;
                if (OriginMarker == null) return;
                if (EndpointMarker == null) return;
            }
            catch (Exception)
            { return; }
            IsValid = true;
        }
        public Vector3D GetOriginPos()
        {
            if (OriginMarker == null) return Vector3.Zero;
            return OriginMarker.GetPosition();
        }
        public Vector3D GetEndpointPos()
        {
            if (EndpointMarker == null) return Vector3.Zero;
            return EndpointMarker.GetPosition();
        }
        public Vector3D GetForwardDirection()
        {
            if(OriginMarker == null) return Vector3.Zero;
            return OriginMarker.WorldMatrix.Forward;
        }
        public Vector3D GetForwardDirectionInverse()
        {
            if (EndpointMarker == null) return Vector3.Zero;
            return EndpointMarker.WorldMatrix.Forward;
        }
        public double GetPrinterLength()
        {
            if (EndpointMarker == null || OriginMarker == null) return -1;
            return Vector3.Distance(OriginMarker.GetPosition(), EndpointMarker.GetPosition());
        }
        public Vector3D GetUpDirection()
        {
            if (OriginMarker == null) return Vector3.Zero;
            return OriginMarker.WorldMatrix.Up;
        }
    }
    public class Printer
    {
        public IMyGridTerminalSystem GridTerminalSystem;
        public Action<string> Echo;
        public PrinterPlatform Platform;
        public PrinterBase Base;
        public int PrinterY;
        public double ShiftMagnitude;
        public double ForwardOffset;
        public Base6Directions.Direction Direction;
        public bool IsValid;

        public Printer(IMyGridTerminalSystem gridTerminalSystem, Action<string> echo, PrinterPlatform platform, PrinterBase @base, int printerY, double shiftMagnitude, double forwardOffset, Base6Directions.Direction direction)
        {
            if (gridTerminalSystem == null) return;
            if (echo == null) return;
            if (platform == null) return;
            if (@base == null) return;
            GridTerminalSystem = gridTerminalSystem;
            Echo = echo;
            Platform = platform;
            Base = @base;
            PrinterY = printerY;
            ShiftMagnitude = shiftMagnitude;
            ForwardOffset = forwardOffset;
            Direction = direction;
            IsValid = true;
        }
        public Path GetOriginEndpointPath(int dataPoint_Amount, bool shiftPathToPrinterY = false)
        {
            if (Base.OriginMarker == null || Base.EndpointMarker == null) return null;
            Vector3D direction = Base.GetForwardDirection();
            Vector3D origin = Base.GetOriginPos() + (direction * ForwardOffset);
            Vector3D endpoint = Base.GetEndpointPos() + (Base.GetForwardDirectionInverse() * ForwardOffset);
            Echo($"ForwardDirection: {Base.GetForwardDirection().ToString()}");
            Echo($"ForwardDirection.Length: {Base.GetForwardDirection().Length()}");
            Echo($"ForwardDirectionInverse: {Base.GetForwardDirectionInverse().ToString()}");
            Echo($"ForwardDirectionInverse.Length: {Base.GetForwardDirectionInverse().Length()}");
            double distance_between_points = Vector3.Distance(origin, endpoint) / dataPoint_Amount;
            List<MyGPSWaypoint> waypoints = new List<MyGPSWaypoint>();
            for (int i = 0; i < dataPoint_Amount + 1; i++)
                waypoints.Add(new MyGPSWaypoint((i + 1).ToString(), origin + ((direction * distance_between_points) * (i))));
            Path path = new Path(waypoints, Direction, Base.GetUpDirection(), 0, 2.5f);
            if (shiftPathToPrinterY)
                path.ShiftPathAbsolute(PrinterY);
            return path;
        }
        public Path GetEndpointOriginPath(int dataPoint_Amount, bool shiftPathToPrinterY = false)
        {
            Path p = GetOriginEndpointPath(dataPoint_Amount, shiftPathToPrinterY);
            p.ReversePathDirection();
            p.ReverseFlightDirection();
            return p;
        }
        public Path OptimizePath(Path path, Vector3 TargetPosition, bool shiftTargetPosition = false)
        {
            Vector3 target = TargetPosition;
            if (shiftTargetPosition)
                target = TargetPosition + (path.Up * (path.ShiftMagnitude * path.PathYShift));
            if (Platform.RemoteControl == null) return null;
            List<MyGPSWaypoint> relevant_waypoints = new List<MyGPSWaypoint>();
            foreach (MyGPSWaypoint waypoint in path.Waypoints)
                if (Vector3.Distance(waypoint.ToVector(), target) < Vector3.Distance(Platform.RemoteControl.GetPosition(), target)) relevant_waypoints.Add(waypoint);
            return new Path(relevant_waypoints, path.FlightDirection, path.Up, path.PathYShift, path.ShiftMagnitude);
        }
    }
    public class Path
    {
        public List<MyGPSWaypoint> Waypoints;
        public Base6Directions.Direction FlightDirection;
        public int PathYShift;
        public readonly float ShiftMagnitude;
        public readonly Vector3D Up;
        public Path(List<MyGPSWaypoint> path, Base6Directions.Direction flightDirection, Vector3D UpDirection, int pathYShift = 0, float shiftMagnitude = 2.5f)
        {
            Waypoints = path;
            FlightDirection = flightDirection;
            Up = UpDirection;
            PathYShift = pathYShift;
            ShiftMagnitude = shiftMagnitude;
        }
        public void ReverseFlightDirection()
        {
            FlightDirection = Base6Directions.GetOppositeDirection(FlightDirection);
        }
        public void ReversePathDirection()
        {
            PathYShift += 1;
            Waypoints.Reverse();
        }
        public void ShiftPathRelative(int shiftDelta)
        {
            int new_shift = PathYShift + shiftDelta;
            ShiftPathAbsolute(new_shift);
        }
        public void ShiftPathAbsolute(int shift)
        {
            List<MyGPSWaypoint> new_path = new List<MyGPSWaypoint>();
            foreach(MyGPSWaypoint waypoint in Waypoints)
            {
                Vector3D vShift = Up * (ShiftMagnitude * PathYShift);
                Vector3D unshifted_pos = waypoint.ToVector() + vShift;
                Vector3D new_shifted_pos = unshifted_pos + (Up * (ShiftMagnitude * -shift));
                new_path.Add(new MyGPSWaypoint(waypoint.Name, new_shifted_pos));
            }
            PathYShift = shift;
            Waypoints = new_path;
        }

        public static Path ShiftPathRelative(Path pathToShift, int shiftDelta)
        {
            int new_shift = pathToShift.PathYShift + shiftDelta;
            return ShiftPathAbsolute(pathToShift, new_shift);
        }

        public static Path ShiftPathAbsolute(Path pathToShift, int shift)
        {
            List<MyGPSWaypoint> new_path = new List<MyGPSWaypoint>();
            foreach (MyGPSWaypoint waypoint in pathToShift.Waypoints)
            {
                Vector3D vShift = pathToShift.Up * (pathToShift.ShiftMagnitude * (0 - pathToShift.PathYShift));
                Vector3D unshifted_pos = waypoint.ToVector() + vShift;
                Vector3D new_shifted_pos = unshifted_pos + (pathToShift.Up * (pathToShift.ShiftMagnitude * shift));
                new_path.Add(new MyGPSWaypoint(waypoint.Name, new_shifted_pos));
            }
            return new Path(new_path, pathToShift.FlightDirection, pathToShift.Up, shift, pathToShift.ShiftMagnitude);
        }
    }
}
