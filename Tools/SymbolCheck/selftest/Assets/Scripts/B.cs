public class B
{
    public static void DeclaredInB() { }

    public void Caller(System.Action cb)
    {
        cb();                          // delegate-typed parameter
        ThisOneIsGenuinelyMissing();   // the one seeded bug the self test must catch
    }
}
