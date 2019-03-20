﻿using System;
using System.Collections.Generic;
using System.Linq;
using NAPS2.Operation;
using NAPS2.Util;
using Ninject;

namespace NAPS2.Lib
{
    public class NinjectOperationFactory : IOperationFactory
    {
        private readonly IKernel kernel;
        private readonly ErrorOutput errorOutput;

        public NinjectOperationFactory(IKernel kernel, ErrorOutput errorOutput)
        {
            this.kernel = kernel;
            this.errorOutput = errorOutput;
        }

        public T Create<T>() where T : IOperation
        {
            var op = kernel.Get<T>();
            op.Error += (sender, args) => errorOutput.DisplayError(args.ErrorMessage, args.Exception);
            return op;
        }
    }
}
