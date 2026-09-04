public class B
{
    public static void DeclaredInB() { }

    public static int RealField;
    public enum Mode { Off, On }

    public void Caller(System.Action cb)
    {
        cb();                          // delegate-typed parameter
        ThisOneIsGenuinelyMissing();   // the one seeded bug the self test must catch
    }

    // Member access on a project type. Everything here resolves except the last line.
    //
    // The seeded bug is deliberately on D, which derives from a base this checker cannot
    // see, because that is the shape of both real breaks: RoadRageLandingDirector and
    // TrafficCarController are MonoBehaviours. An earlier fixture put it on B, which has
    // no base at all, and so passed even with the member check regressed to its original
    // broken form - the fixture has to exercise the path that actually failed.
    public void MemberAccess()
    {
        B.RealField = 1;                // static field on this type
        var m = B.Mode.On;              // nested enum, then a member of it
        var d = C.Inherited;            // inherited from a project base type
        D.Destroy(null);                // inherited Unity static, through an unseen base
        var r = D.RealStatic;           // declared on a type with an unseen base
        D.ThisMemberDoesNotExist = 2;   // the seeded member bug
        E.EditorOnlyMember = 3;         // declared, but only inside #if UNITY_EDITOR
    }
}

/// Guards the parse options. Roslyn drops declarations inside inactive #if regions, so
/// parsing without UNITY_EDITOR defined makes EditorOnlyMember invisible while the use of
/// it above stays visible - and the checker reports correct code as broken. E has no
/// unseen base, so nothing else can excuse the member: if the symbol is not defined at
/// parse time this is flagged, the fixture then has two member findings instead of one,
/// and the self test fails.
public class E
{
#if UNITY_EDITOR
    public static int EditorOnlyMember;
#endif
}

public class BaseWithMember
{
    public static int Inherited;
}

public class C : BaseWithMember
{
}

public class D : SomeUnityBaseTheCheckerCannotSee
{
    public static int RealStatic;
}
