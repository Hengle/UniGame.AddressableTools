﻿namespace UniGame.Addressables.Reactive
{
    using System;
    using System.Collections;
    using UniCore.Runtime.ProfilerTools;
    using UniModules.UniCore.Runtime.Attributes;
    using UniModules.UniCore.Runtime.DataFlow;
    using UniModules.UniCore.Runtime.ObjectPool.Runtime.Extensions;
    using UniModules.UniCore.Runtime.ObjectPool.Runtime.Interfaces;
    using UniModules.UniGame.AddressableTools.Runtime.Extensions;
    using UniModules.UniGame.Core.Runtime.Extension;
    using UniModules.UniGame.Core.Runtime.Rx;
    using UniModules.UniRoutine.Runtime;
    using UniModules.UniGame.Core.Runtime.DataFlow.Interfaces;
    using UniRx;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using Object = UnityEngine.Object;

    [Serializable]
    public class AddressableObservable<TAddressable,TData,TApi> : 
        IAddressableObservable<TApi> ,
        IPoolable
        where TAddressable : AssetReference 
        where TData : Object
        where TApi : class
    {
        #region inspector

        [SerializeField] protected RecycleReactiveProperty<TApi> value = new RecycleReactiveProperty<TApi>();

        [SerializeField] protected TAddressable reference;

        [SerializeField] protected FloatRecycleReactiveProperty progress = new FloatRecycleReactiveProperty();
     
        #if ODIN_INSPECTOR
        [Sirenix.OdinInspector.InlineEditor(Sirenix.OdinInspector.InlineEditorModes.SmallPreview)]
        #endif
        [ReadOnlyValue]
        [SerializeField] protected TData asset;
        
        [SerializeField]
        protected LifeTimeDefinition lifeTimeDefinition = new LifeTimeDefinition();

        #endregion
        
        private RoutineHandle routineHandler;

        protected RecycleReactiveProperty<bool> isReady = new RecycleReactiveProperty<bool>();
        
        protected RecycleReactiveProperty<AsyncOperationStatus> status = new RecycleReactiveProperty<AsyncOperationStatus>();
        
        #region public properties

        public IReadOnlyReactiveProperty<AsyncOperationStatus> Status => status;
        
        public IReadOnlyReactiveProperty<float> Progress => progress;

        public IReadOnlyReactiveProperty<bool> IsReady => isReady;

        public IReadOnlyReactiveProperty<TApi> Value => value;
        
        #endregion
        
        #region public methods
        
        /// <summary>
        /// initialize property with target Addressable Asset 
        /// </summary>
        /// <param name="addressable"></param>
        public void Initialize(TAddressable addressable)
        {
            lifeTimeDefinition = lifeTimeDefinition ?? new LifeTimeDefinition();
            progress = progress ?? new FloatRecycleReactiveProperty();
            status = status ?? new RecycleReactiveProperty<AsyncOperationStatus>();
            value = value ?? new RecycleReactiveProperty<TApi>();
            
            lifeTimeDefinition.Release();
            
            reference = addressable;
            
            lifeTimeDefinition.AddCleanUpAction(CleanUp);
            
        }

        public IDisposable Subscribe(IObserver<TApi> observer)
        {
            if (!ValidateReference()) {
                value.Value = default;
                return Disposable.Empty;
            }
            
            var disposableValue = value.
                Subscribe(observer);

            routineHandler = LoadReference(lifeTimeDefinition).
                Execute().
                AddTo(lifeTimeDefinition);
            
            return disposableValue;
        }

        public void Dispose() => this.Despawn();
        
        public void Release() => lifeTimeDefinition.Terminate();

        #endregion
        
        #region private methods

        private bool ValidateReference()
        {
            if (reference == null || reference.RuntimeKeyIsValid() == false) {
                GameLog.LogWarning($"AddressableObservable : LOAD Addressable Failed {reference}");
                status.Value = AsyncOperationStatus.Failed;
                return false;
            }

            return true;
        }
        
        private IEnumerator LoadReference(ILifeTime lifeTime)
        {
            if (!ValidateReference()) {
                value.Value = default;
                yield break;
            }

            var targetType = typeof(TData);
            var apiType = typeof(TApi);

            var isComponent = targetType.IsComponent() || apiType.IsComponent();

            var routine = isComponent
                ? LoadHandle<GameObject>(lifeTime,x => value.Value = x.GetComponent<TApi>()) 
                : LoadHandle<TData>(lifeTime,x => value.Value = x as TApi);
            
            yield return routine;
        }

        private IEnumerator LoadHandle<TValue>(ILifeTime lifeTime,Action<TValue> result) 
            where TValue : Object
        {
            var handler = reference.LoadAssetAsyncOrExposeHandle<TValue>(out var yetRequested)
                .AddTo(lifeTime, yetRequested);
            
            while (handler.IsDone == false) {
                progress.Value = handler.PercentComplete;
                status.Value   = handler.Status;
                yield return null;
            }
            
            isReady.Value = true;
            result(handler.Result);
        }

        private void CleanUp()
        {
            routineHandler.Cancel();
            status.Release();
            status.Value = AsyncOperationStatus.None;
            
            progress.Release();
            progress.Value = 0;
            
            isReady.Release();
            isReady.Value = false;
            
            reference = null;
            
            value.Release();
        }
        
        #endregion

        #region deconstructor
        
        ~AddressableObservable() => Release();
        
        #endregion
    }
}
