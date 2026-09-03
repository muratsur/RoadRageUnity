// Fixture for SymbolCheck's self test. Not compiled by Unity: Unity only compiles
// scripts under Assets/, and this lives under Tools/.
//
// Every construct here is one that could plausibly make the checker cry wolf. The
// only genuinely undefined call in the fixture is in B.cs.
using System;

public class A : SomeUnityBaseTheCheckerCannotSee
{
    private int counter;
    private Action onDone;

    private void Update()
    {
        Helper();                    // local function, called above its declaration
        void Helper() { counter++; }

        onDone();                    // delegate-typed field

        Action local = () => counter--;
        local();                     // delegate-typed local

        DeclaredInB();               // declared in another file of the project

        var n = nameof(counter);     // contextual keyword that parses as a call

        StaticHelper();              // static on the calling class

        Destroy(null);               // a real Unity API: external, not an error
    }

    private static void StaticHelper() { }
}
