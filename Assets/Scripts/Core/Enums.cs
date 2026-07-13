namespace Core
{
    /// <summary>
    /// The Swerve Module style to use
    /// </summary>
    public enum ModuleType
    {
        InvertedCorner,
        StandardCorner,
        Inverted,
        Standard,
        LowProfile
    }

    public enum ShaftType
    {
        Hex,
        Spline,
        Dead
    }

    public enum SpacerType
    {
        Hex,
        QuarterInch,
        Spline
    }

    public enum PlateType
    {
        Rectangle,
        Triangle,
        CornerBracket,
        Bracket
    }

    public enum BumperType
    {
        Modern,
        Legacy
    }

    public enum Cameras
    {
        FirstPerson,
        FirstPersonReversed,
        ThirdPerson,
        ReversedThirdPerson,
        DriverStation
    }

    public enum GearType
    {
        Pinion,
        Hex,
        Spline
    }

    public enum StationNum
    {
        One,
        Two,
        Three
    }

    public enum TrackingType
    {
        TrackRobot,
        PointForward
    }

    public enum TargetType
    {
        Closest,
        Furthest,
        Preset,
        Custom
    }

    public enum AimAtWhen
    {
        Always,
        AtSetpoint,
        WhenPressing,
        WithinRange,
    }

    public enum TargetWhen
    {
        Always,
        AtSetpoint,
    }

    public enum TargetingMethod
    {
        PointAtOffset,
        Interpolation
    }

    public enum BumperVariants
    {
        Side,
        Corner,
        Lift
    }

    public enum PlateMaterials
    {
        Aluminum,
        Polycarb,
        Abs
    }

    public enum AutoAlignType
    {
        Release,
        Button
    }
    
    public enum ControlType
    {
        Toggle,
        Hold,
        LastPressed,
        SequenceStart,
        Sequence,
    }

    public enum ElevatorType
    {
        Cascade,
        Continuous
    }

    public enum ArmModel
    {
        Single,
        SplitParallel,
        SingleTwoByTwo,
        None
    }
    
    public enum SequenceType
    {
        NextPress,
        Delay,
        End
    }

    public enum SpawnType
    {
        Threshold,
        Distance
    }

    public enum WheelTypes
    {
        TwoInSquish,
        TwoInStealth,
        TwoQuarterInSquish,
        ThreeInSquish,
        ThreeInStealth,
        FourInSquish,
        FourInStealth,
        FourInOmni,
        FourInBillet,
        FiveInFlywheel,
        SixInOmni,
    }

    public enum MotorTypes
    {
        AngryFish,
        Eon,
        Eon55,
        Midget,
        PowerfulBird,
        Tornado
    }

    /// <summary>
    /// Tube sizing names.
    /// </summary>
    public enum TubeType
    {
        OneXTwoXEighth,
        TwoXTwoXEighth,
        OneXOneXEighth
    }

    /// <summary>
    /// Units that can be used to generate Parts.
    /// </summary>
    public enum Units
    {
        Inch,
        Meter,
        Centimeter,
        Millimeter
    }

    public enum PieceNames
    {
        Coral,
        Algae,
        Fuel
    }
    
    public enum ScoreCounterType
    {
        None,
        Fuel,
        Coral,
        Algae
    }

    public enum GamePieceState
    {
        World,
        Stationary,
        Moving
    }

    public enum NodeType
    {
        Intake,
        Transfer,
        OutTake,
        Hp
    }

    public enum NodeControlType
    {
        Hold,
        Tap,
        AlwaysPerform,
    }

    public enum NodeState
    {
        Stowing,
        Intaking,
        Transfering,
        Outaking,
    }

    public enum Direction
    {
        Forward,
        Sideways,
        Up
    }

    public enum RobotCommand
    {
        // Rebuilt
        Shoot,
        Intake,
        PassLeft,
        PassRight,
        Hub,
        RobotSpecial,
        HumanPlayerDump,
        
        // General
        FlipCamera,
        Restart,
        Menu,
        
        // Reefscape
        AutoAlign,
        L1,
        L2,
        L3,
        L4,
        Barge,
        AlgaeHigh,
        AlgaeLow,
        AlgaeHold,
        Climb
    }
    
    public enum BindingKind
    {
        Button,
        Drive,
        Rotate
    }

    public enum DeviceKind
    {
        Keyboard,
        Gamepad
    }
    
    public enum PlayMode
    {
        OneVsZero,
        TwoVsZero,
        OneVsOne,
        ThreeVsZero,
        TwoVsTwo
    }
    
    public enum HumanPlayerType
    {
        Bucket,
        Dumper
    }
    
    public enum LaunchSource
    {
        None,
        Robot,
        HumanPlayer
    }
    
    public enum AllianceColor
    {
        None,
        Blue,
        Red
    }
    
    public enum AimRegionId
    {
        BlueAlliance,
        RedAlliance,
        Neutral
    }
    
    public enum FrameRateMode
    {
        FPS30,
        FPS60,
        FPS75,
        FPS90,
        FPS120,
        FPS144,
        FPS165,
        FPS240,
        Unlimited,
        VSync
    }
        
    public enum WindowMode
    {
        Windowed = 0,
        BorderlessFullscreen = 1,
        ExclusiveFullscreen = 2
    }
    
    public enum DetailPanelMode
    {
        Inactive,
        JoinPrompt,
        SelectingRobot,
        EditingDetails,
        Ready
    }
}