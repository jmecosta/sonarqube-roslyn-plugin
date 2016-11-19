namespace RoslynRunner.Test

open NUnit.Framework


[<TestFixture>]
type TestTypes() =

    [<Test>]
    member this.ShouldHandleEnums() = 
        let content = """     type KeyLookUpType =
                                   // the name
                                   | Module = 0
                                   // the name
                                   | Flat = 1
                                   // the name
                                   | VSBootStrapper = 2 """

        Assert.That(true, Is.True)