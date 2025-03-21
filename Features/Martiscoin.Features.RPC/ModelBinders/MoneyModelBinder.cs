﻿using System.Threading.Tasks;
using Martiscoin.NBitcoin;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Martiscoin.Features.RPC.ModelBinders
{
    public class MoneyModelBinder : IModelBinder, IModelBinderProvider
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(Money))
            {
                return Task.CompletedTask;
            }

            ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            string key = val.FirstValue;
            if (key == null)
            {
                return Task.CompletedTask;
            }
            return Task.FromResult(Money.Parse(key));
        }

        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(Money))
                return this;
            return null;
        }
    }
}
