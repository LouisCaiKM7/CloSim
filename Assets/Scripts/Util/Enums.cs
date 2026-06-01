namespace Util
{
    /// <summary>
    /// The Swerve Module style to use
    /// </summary>
    public enum ModuleType
    {
        invertedCorner,
        standardCorner,
        inverted,
        standard,
        lowProfile
    }

    public enum ShaftType
    {
        Hex,
        Spline,
        Dead
    }

    public enum spacerType
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
        TBracket
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
        pinion,
        hex,
        spline
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

    public enum AutoAlginType
    {
        release,
        button
    }

    /// <summary>
    /// The type of setpoint that is being used
    /// </summary>
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

    /// <summary>
    /// The continuation requirement for the sequence type of setpoint
    /// </summary>
    public enum SequenceType
    {
        nextPress,
        delay,
        end
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
        Outake,
        HP
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
        Intakeing,
        Transfering,
        Outaking,
    }

    public enum Direction
    {
        forward,
        sideways,
        up
    }

    public enum ControllerInputs
    {
        A,
        B,
        X,
        Y,
        DpadUp,
        DpadDown,
        DpadLeft,
        DpadRight,
        LeftTrigger,
        RightTrigger,
        LeftBumper,
        RightBumper,
    }
    
    public enum KeyboardInputs
    {
        D1,
        D2,
        D3,
        D4,
        D5,
        D6,
        D7,
        D8,
        D9,
        D0,
        B,
        C,
        E,
        F,
        G,
        H,
        I,
        K,
        M,
        N,
        O,
        P,
        Q,
        T,
        U,
        V,
        X,
        Y,
        Z,
        LeftShift,
        LeftControl,
        LeftAlt,
        Tab,
        Space,
        Escape,
        UpArrow,
        DownArrow,
        LeftArrow,
        RightArrow,
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
}