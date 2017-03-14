﻿// ***********************************************************************
// Copyright (c) 2012 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.Threading;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Commands;

namespace NUnit.Framework.Internal.Execution
{
    /// <summary>
    /// A SimpleWorkItem represents a single test case and is
    /// marked as completed immediately upon execution. This
    /// class is also used for skipped or ignored test suites.
    /// </summary>
    public class SimpleWorkItem : WorkItem
    {
        TestMethod _testMethod;

        /// <summary>
        /// Construct a simple work item for a test.
        /// </summary>
        /// <param name="test">The test to be executed</param>
        /// <param name="filter">The filter used to select this test</param>
        public SimpleWorkItem(TestMethod test, ITestFilter filter) : base(test, filter)
        {
            _testMethod = test;
        }

        /// <summary>
        /// Method that performs actually performs the work.
        /// </summary>
        protected override void PerformWork()
        {
            var command = Test.RunState == RunState.Runnable || 
                Test.RunState == RunState.Explicit && Filter.IsExplicitMatch(Test)
                    ? MakeTestCommand()
                    : new SkipCommand(Test);

            try
            {
                Result = command.Execute(Context);
            }
            finally
            {
                WorkItemComplete();
            }
        }

        /// <summary>
        /// Creates a test command for use in running this test.
        /// </summary>
        /// <returns>A TestCommand</returns>
        private TestCommand MakeTestCommand()
        {
            // Command to execute test
            TestCommand command = new TestMethodCommand(_testMethod);

            var method = _testMethod.Method;

            // Add any wrappers to the TestMethodCommand
            foreach (IWrapTestMethod wrapper in method.GetCustomAttributes<IWrapTestMethod>(true))
                command = wrapper.Wrap(command);

            // Create TestActionCommands using attributes of the method
            foreach (ITestAction action in Test.Actions)
                if (action.Targets == ActionTargets.Default || (action.Targets & ActionTargets.Test) == ActionTargets.Test)
                    command = new TestActionCommand(command, action);

            // Wrap in SetUpTearDownCommand
            command = new SetUpTearDownCommand(command);

            // In the current implementation, upstream actions only apply to tests. If that should change in the future,
            // then actions would have to be tested for here. For now we simply assert it in Debug. We allow 
            // ActionTargets.Default, because it is passed down by ParameterizedMethodSuite.
            int index = Context.UpstreamActions.Count;
            while (--index >= 0)
            {
                ITestAction action = Context.UpstreamActions[index];
                System.Diagnostics.Debug.Assert(
                    action.Targets == ActionTargets.Default || (action.Targets & ActionTargets.Test) == ActionTargets.Test,
                    "Invalid target on upstream action: " + action.Targets.ToString());

                command = new TestActionCommand(command, action);
            }

            // Add wrappers that apply before setup and after teardown
            foreach (ICommandWrapper decorator in method.GetCustomAttributes<IWrapSetUpTearDown>(true))
                command = decorator.Wrap(command);

            // Add command to set up context using attributes that implement IApplyToContext
            IApplyToContext[] changes = method.GetCustomAttributes<IApplyToContext>(true);
            if (changes.Length > 0)
                command = new ApplyChangesToContextCommand(command, changes);

            return command;
        }
    }
}
