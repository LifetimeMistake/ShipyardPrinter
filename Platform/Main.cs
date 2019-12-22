using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
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
        public bool Initialize()
        {   
            printer = new Printer(this, new PrinterPlatform(GridTerminalSystem, Echo, "Conn Front A", "Conn Front B", "Conn Back A", "Conn Back B", "RC"),
                    new PrinterBase(GridTerminalSystem, Echo, "Conn Origin A", "Conn Origin B", "Conn Endpoint A", "Conn Endpoint B", "Marker Origin", "Marker Endpoint"), 0, 19,
                    2.5, 15, Base6Directions.Direction.Forward);
            if (!printer.IsValid)
            {
                Echo("Failed to initialize the printer instance.");
                printer = null;
                return false;
            }
            if (!printer.Platform.IsValid)
            {
                Echo("Failed to initialize the printer platform instance");
                printer = null;
                return false;
            }
            if (!printer.Base.IsValid)
            {
                Echo("Failed to initialize the printer base instance");
                printer = null;
                return false;
            }

            Echo("Printer instance initialized successfully.");
            return true;
        }
        public void Main(string argument)
        {
            if (printer == null)
                if (!Initialize()) return;
            if (!printer.IsValid)
                if (!Initialize()) return;

            if (printer.CancelTask)
                Echo("Canceling printer task...");
            if (printer.Accelerate)
                Echo("WARNING: Script Acceleration is online.");
            Echo($"Current state: {printer.TaskState}");
            
            if(argument == "start")
            {
                printer.TaskState = PrinterTaskState.Process_Start;
            }

            if(argument == "cancel")
            {
                printer.CancelTask = true;
            }

            printer.ProcessTasks();

            Echo("Runtime finished.");
            Echo($"Instruction count: {Runtime.CurrentInstructionCount}");
            Echo($"Last execution time: {Runtime.LastRunTimeMs}ms");
        }
    }
    public class MyAutoGyro
    {
        public enum AutoGyroMode
        {
            Forward,
            Backward,
            Rocket
        }

        private IMyGridTerminalSystem GridTerminalSystem { get; set; }
        private Action<string> Echo { get; set; }
        private IMyProgrammableBlock Me { get; set; }
        private double CurrentAngle { get; set; }

        public List<IMyGyro> Gyros { get; protected set; }
        public IMyRemoteControl RC { get; set; }
        public int MaxGyroAmount { get; set; }
        public double CTRL_COEFF { get; set; }
        public float accuracyDeg { get; set; }

        public Vector3 TargetDirection { get; protected set; }
        public AutoGyroMode gyroMode { get; set; }

        private void Init()
        {
            GridTerminalSystem = null;
            Echo = null;
            Me = null;
            Gyros = new List<IMyGyro>();
            RC = null;
            MaxGyroAmount = 99;
            CTRL_COEFF = 0.5;
            accuracyDeg = 0.01f;
            TargetDirection = Vector3.Zero;
            gyroMode = AutoGyroMode.Forward;
        }
        public void UpdateGyroList()
        {
            GridTerminalSystem.GetBlocksOfType(Gyros, gyro => gyro.CustomData.Contains("PRINTER_GYRO_ALLOW"));
            if (Gyros.Count > MaxGyroAmount)
                Gyros.RemoveRange(MaxGyroAmount, Gyros.Count - MaxGyroAmount);
        }
        public void GyrosOff()
        {
            foreach (IMyGyro g in Gyros)
            { g.GyroOverride = false; }
        }
        public void GyroMain()
        {
            if (Gyros.Count == 0)
                UpdateGyroList();
            if (Gyros.Count == 0)
            {
                Echo("No functional gyroscopes found.");
                return;
            }

            if (RC == null)
            {
                Echo("No functional remote control blocks found.");
                GyrosOff();
                return;
            }

            if (TargetDirection == Vector3.Zero)
            {
                Echo("No target was set.");
                GyrosOff();
                return;
            }

            Matrix or;
            RC.Orientation.GetMatrix(out or);

            Vector3D down;
            if (gyroMode == AutoGyroMode.Rocket)
                down = or.Backward;
            else if (gyroMode == AutoGyroMode.Backward)
                down = or.Backward;
            else if (gyroMode == AutoGyroMode.Forward)
                down = or.Forward;
            else
                down = or.Down;

            Vector3D vDirection = TargetDirection;
            vDirection.Normalize();

            Echo($"gyro count: {Gyros.Count}");

            foreach (IMyGyro g in Gyros)
            {
                g.Orientation.GetMatrix(out or);
                Vector3D localDown = Vector3D.Transform(down, MatrixD.Transpose(or));
                Vector3D localGrav = Vector3D.Transform(vDirection, MatrixD.Transpose(g.WorldMatrix.GetOrientation()));

                var rot = Vector3D.Cross(localDown, localGrav);
                double dot2 = Vector3D.Dot(localDown, localGrav);
                double ang = rot.Length();
                ang = Math.Atan2(ang, Math.Sqrt(Math.Max(0.0, 1.0 - ang * ang)));
                if (dot2 < 0) ang += Math.PI;
                CurrentAngle = ang;
                if (ang < accuracyDeg)
                {
                    g.GyroOverride = false;
                    continue;
                }

                double ctrl_vel = g.GetMaximum<float>("Yaw") * (ang / Math.PI) * CTRL_COEFF;
                ctrl_vel = Math.Min(g.GetMaximum<float>("Yaw"), ctrl_vel);
                ctrl_vel = Math.Max(0.01, ctrl_vel);
                rot.Normalize();
                rot *= ctrl_vel;
                float pitch = (float)rot.GetDim(0);
                g.SetValueFloat("Pitch", pitch);

                float yaw = -(float)rot.GetDim(1);
                g.SetValueFloat("Yaw", yaw);

                float roll = -(float)rot.GetDim(2);
                g.SetValueFloat("Roll", roll);

                g.GyroOverride = true;

                Echo($"Method 1: {pitch},{yaw},{roll}");
            }
        }
        public bool IsAligned(float midAngleRad)
        {
            return (CurrentAngle < midAngleRad);
        }
        public bool IsAligned()
        {
            return (CurrentAngle < accuracyDeg);
        }
        public void SetTarget(Vector3 Direction)
        {
            TargetDirection = Direction;
        }
        public MyAutoGyro(AutoGyroMode mode, MyGridProgram program, string RC_name)
        {
            Init();
            gyroMode = mode;
            if (program == null)
                return;
            GridTerminalSystem = program.GridTerminalSystem;
            Echo = program.Echo;
            Me = program.Me;

            List<IMyRemoteControl> RCs = new List<IMyRemoteControl>();
            GridTerminalSystem.GetBlocksOfType(RCs, b => b.CustomName == RC_name);
            if (RCs.Count == 0)
            {
                Echo("No functional remote control blocks found.");
                return;
            }

            RC = RCs.First();

            UpdateGyroList();
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
        public bool Connected()
        {
            return (Left.Status == MyShipConnectorStatus.Connected && Right.Status == MyShipConnectorStatus.Connected);
        }
        public bool Connectable()
        {
            return (Left.Status == MyShipConnectorStatus.Connectable || Left.Status == MyShipConnectorStatus.Connected)
                && (Right.Status == MyShipConnectorStatus.Connectable || Right.Status == MyShipConnectorStatus.Connected);
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
            catch (Exception)
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
            if (OriginMarker == null) return Vector3.Zero;
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
        private MyGridProgram Program;
        public IMyGridTerminalSystem GridTerminalSystem;
        public Action<string> Echo;
        public PrinterPlatform Platform;
        public PrinterBase Base;
        public MyAutoGyro AutoGyro;
        public int PrinterY;
        public int PrinterHeight;
        public double ShiftMagnitude;
        public double ForwardOffset;
        public Base6Directions.Direction Direction;
        public bool IsValid;
        public bool Accelerate;
        public bool CancelTask;
        public PrinterTaskState TaskState;

        public Printer(MyGridProgram program, PrinterPlatform platform, PrinterBase @base, int printerY, int printerHeight, double shiftMagnitude, double forwardOffset, Base6Directions.Direction direction)
        {
            if (program == null) return;
            if (program.GridTerminalSystem == null) return;
            if (program.Echo == null) return;
            if (platform == null) return;
            if (@base == null) return;
            GridTerminalSystem = program.GridTerminalSystem;
            Echo = program.Echo;
            Platform = platform;
            Base = @base;
            PrinterY = printerY;
            PrinterHeight = printerHeight;
            ShiftMagnitude = shiftMagnitude;
            ForwardOffset = forwardOffset;
            Direction = direction;
            AutoGyro = new MyAutoGyro(MyAutoGyro.AutoGyroMode.Rocket, program, platform.RemoteControl.CustomName);
            TaskState = PrinterTaskState.Construction_Completed;
            Program = program;
            IsValid = true;
        }
        public Path GetOriginEndpointPath(int dataPoint_Amount, bool shiftPathToPrinterY = false)
        {
            if (Base.OriginMarker == null || Base.EndpointMarker == null) return null;
            Vector3D direction = Base.GetForwardDirection();
            Vector3D origin = Base.GetOriginPos() + (direction * ForwardOffset);
            Vector3D endpoint = Base.GetEndpointPos() + (Base.GetForwardDirectionInverse() * ForwardOffset);
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
        public bool IsYShiftSafe()
        {
            Vector3 Origin = (Base.GetOriginPos() + Base.GetUpDirection() * (ShiftMagnitude * (PrinterY * -1))) + (Base.GetForwardDirection() * ForwardOffset);
            if (Vector3.Distance(Origin, Platform.RemoteControl.GetPosition()) < 0.5) return true;
            return false;
        }
        public bool ShiftPrinterY(int shiftDelta)
        {
            if (!IsYShiftSafe())
            {
                Echo("ShiftPrinterY: Safeguard triggered, Y shift not safe");
                return false;
            }
            int newY = PrinterY + shiftDelta;
            List<MyGPSWaypoint> waypoints = new List<MyGPSWaypoint>();
            Path path = null;
            Vector3D hitbox_safe_distance = Base.GetForwardDirection() * 1.5;
            if (newY == PrinterY)
            {
                Echo("ShiftPrinterY: newY = currentY; Task cancelled");
                return false;
            }
            waypoints.Add(new MyGPSWaypoint($"yshift_origin_safe", ((Base.GetOriginPos() + Base.GetUpDirection() * (ShiftMagnitude * (PrinterY * -1))) + (Base.GetForwardDirection() * ForwardOffset) + hitbox_safe_distance)));
            if (PrinterY < newY)
            {
                for (int i = PrinterY; i < newY + 1; i++)
                {
                    Vector3 Local_Origin = ((Base.GetOriginPos() + Base.GetUpDirection() * (ShiftMagnitude * (i * -1))) + (Base.GetForwardDirection() * ForwardOffset) + hitbox_safe_distance);
                    MyGPSWaypoint waypoint = new MyGPSWaypoint(i.ToString(), Local_Origin);
                    waypoints.Add(waypoint);
                }
                path = new Path(waypoints, Base6Directions.Direction.Down, Base.GetUpDirection(), newY, 2.5f);
            }
            else if (PrinterY > newY)
            {
                for (int i = PrinterY; i > newY - 1; i--)
                {
                    Vector3 Local_Origin = ((Base.GetOriginPos() + Base.GetUpDirection() * (ShiftMagnitude * (i * -1))) + (Base.GetForwardDirection() * ForwardOffset) + hitbox_safe_distance);
                    MyGPSWaypoint waypoint = new MyGPSWaypoint(i.ToString(), Local_Origin);
                    waypoints.Add(waypoint);
                }
                path = new Path(waypoints, Base6Directions.Direction.Up, Base.GetUpDirection(), newY, 2.5f);
            }
            waypoints.Add(new MyGPSWaypoint($"yshift_endpoint_safe", (Base.GetOriginPos() + Base.GetUpDirection() * (ShiftMagnitude * (newY * -1))) + (Base.GetForwardDirection() * ForwardOffset)));
            PrinterY = newY;
            Echo("Y Shift commencing...");
            Echo($"{waypoints.Count} datapoints sent to the printer platform.");
            Platform.SetTask(path);
            return true;
        }
        public void ProcessTasks()
        {
            switch (TaskState)
            {
                case PrinterTaskState.Process_Start:
                    
                    if(IsYShiftSafe())
                    {
                        Platform.ConnectorsBack.PowerOff();
                        TaskState = PrinterTaskState.YShift_Processing;
                        ShiftPrinterY(PrinterHeight);
                        return;
                    }
                    Echo("Failed to start the printer");
                    break;
                case PrinterTaskState.PrintLayer_Stage1:
                    if ((Vector3D.Distance(GetEndpointOriginPath(10, true).Waypoints[0].ToVector(), Platform.RemoteControl.GetPosition()) < 0.5)
                        && !Platform.RemoteControl.IsAutoPilotEnabled)
                    {
                        TaskState = PrinterTaskState.YShift_Await;
                        Path path_eo = OptimizePath(GetEndpointOriginPath(10, true), Base.GetOriginPos(), true);
                        Platform.SetTask(path_eo);
                    }
                    break;
                case PrinterTaskState.YShift_Await:
                    if (!IsYShiftSafe()) break;
                    if (PrinterY == 0)
                    {
                        TaskState = PrinterTaskState.Gyro_Adjust;
                        break;
                    }

                    if (CancelTask)
                        ShiftPrinterY(-PrinterY);
                    else
                        ShiftPrinterY(-1);
                    TaskState = PrinterTaskState.YShift_Processing;
                    break;
                case PrinterTaskState.YShift_Processing:
                    if (IsYShiftSafe() && !Platform.RemoteControl.IsAutoPilotEnabled)
                        TaskState = PrinterTaskState.Gyro_Adjust;
                    break;
                case PrinterTaskState.Gyro_Adjust:
                    Path p = GetOriginEndpointPath(10, true);
                    Vector3D direction = Vector3D.Normalize(Platform.RemoteControl.GetPosition() - p.Waypoints.Last().ToVector());
                    AutoGyro.SetTarget(direction);
                    Program.Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    Accelerate = true; // Warning: Heavy sim speed impact.
                    foreach (IMyGyro g in AutoGyro.Gyros)
                        g.GyroPower = 0.5f;
                    AutoGyro.GyroMain();
                    TaskState = PrinterTaskState.Gyro_Adjusting;
                    break;
                case PrinterTaskState.Gyro_Adjusting:
                    if(!AutoGyro.IsAligned())
                    {
                        Echo("Adjusting platform direction...");
                        AutoGyro.GyroMain();
                        return;
                    }
                    TaskState = PrinterTaskState.Gyro_Adjusted;
                    break;
                case PrinterTaskState.Gyro_Adjusted:
                    Program.Runtime.UpdateFrequency = UpdateFrequency.None;
                    Accelerate = false;
                    foreach (IMyGyro g in AutoGyro.Gyros)
                        g.GyroPower = 1f;
                    if (PrinterY == 0)
                    {
                        TaskState = PrinterTaskState.Construction_Completed_Dock;
                        break;
                    }

                    TaskState = PrinterTaskState.PrintLayer_Stage1;
                    Path path_oe = OptimizePath(GetOriginEndpointPath(10, true), Base.GetEndpointPos(), true);
                    Platform.SetTask(path_oe);
                    break;
                case PrinterTaskState.Construction_Completed_Dock:
                    CancelTask = false;
                    Platform.ConnectorsBack.PowerOn();
                    if (Platform.ConnectorsBack.Connectable())
                        Platform.ConnectorsBack.Lock();
                    if(Platform.ConnectorsBack.Connected())
                        TaskState = PrinterTaskState.Construction_Completed;
                    break;
                case PrinterTaskState.Construction_Completed:
                    Echo("Construction completed");
                    break;
            }
        }
    }
    public enum PrinterTaskState
    {
        Process_Start,
        PrintLayer_Stage1,
        YShift_Await,
        YShift_Processing,
        Gyro_Adjust,
        Gyro_Adjusting,
        Gyro_Adjusted,
        Construction_Completed,
        Construction_Completed_Dock
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
            foreach (MyGPSWaypoint waypoint in Waypoints)
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
