using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace MyAutoGyro_Test
{

    public class Program : MyGridProgram
    {
        MyAutoGyro AutoGyro;

        public void Main()
        {
            if(AutoGyro == null)
            {
                AutoGyro = new MyAutoGyro(MyAutoGyro.AutoGyroMode.Rocket, this, "Remote Control");
                Vector3D target = AutoGyro.RC.GetPosition() + (AutoGyro.RC.WorldMatrix.Up * 100);
                Me.CustomData = $"GPS:test:{target.X}:{target.Y}:{target.Z}:";
                AutoGyro.SetTarget(Vector3.Normalize(AutoGyro.RC.GetPosition() - target));
                Runtime.UpdateFrequency = UpdateFrequency.Update1;
            }

            AutoGyro.GyroMain();

            Echo(Runtime.LastRunTimeMs.ToString());
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
}
