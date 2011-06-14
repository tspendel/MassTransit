// Copyright 2007-2011 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.Saga
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading;
	using Exceptions;
	using log4net;
	using MassTransit.Pipeline;
	using Util;

	public class InMemorySagaRepository<TSaga> :
		ISagaRepository<TSaga>
		where TSaga : class, ISaga
	{
		static readonly ILog _log = LogManager.GetLogger(typeof (InMemorySagaRepository<TSaga>));
		IndexedSagaDictionary<TSaga> _sagas;

		public InMemorySagaRepository()
		{
			_sagas = new IndexedSagaDictionary<TSaga>();
		}

		public IEnumerable<Action<IConsumeContext<TMessage>>> GetSaga<TMessage>(IConsumeContext<TMessage> context, Guid sagaId,
		                                                                        InstanceHandlerSelector<TSaga, TMessage>
		                                                                        	selector, ISagaPolicy<TSaga, TMessage> policy)
			where TMessage : class
		{
			bool needToLeave = true;
			Monitor.Enter(_sagas);
			try
			{
				TSaga instance = _sagas[sagaId];

				if (instance == null)
				{
					if (policy.CanCreateInstance(context))
					{
						yield return x =>
							{
								if (_log.IsDebugEnabled)
									_log.DebugFormat("SAGA: {0} Creating New {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
										typeof (TMessage).ToFriendlyName());

								try
								{
									instance = policy.CreateInstance(x, sagaId);
									_sagas.Add(instance);

									Monitor.Exit(_sagas);
									needToLeave = false;

									foreach (var callback in selector(instance, x))
									{
										callback(x);
									}

									if (policy.CanRemoveInstance(instance))
										_sagas.Remove(instance);
								}
								catch (Exception ex)
								{
									var sex = new SagaException("Create Saga Instance Exception", typeof (TSaga), typeof (TMessage), sagaId, ex);
									if (_log.IsErrorEnabled)
										_log.Error(sex);

									throw sex;
								}
							};
					}
					else
					{
						if (_log.IsDebugEnabled)
							_log.DebugFormat("SAGA: {0} Ignoring Missing {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
								typeof (TMessage).ToFriendlyName());
					}
				}
				else
				{
					if (policy.CanUseExistingInstance(context))
					{
						Monitor.Exit(_sagas);
						needToLeave = false;

						yield return x =>
							{
								if (_log.IsDebugEnabled)
									_log.DebugFormat("SAGA: {0} Using Existing {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
										typeof (TMessage).ToFriendlyName());

								try
								{
									foreach (var callback in selector(instance, x))
									{
										lock (instance)
											callback(x);
									}

									if (policy.CanRemoveInstance(instance))
										_sagas.Remove(instance);
								}
								catch (Exception ex)
								{
									var sex = new SagaException("Existing Saga Instance Exception", typeof (TSaga), typeof (TMessage), sagaId, ex);
									if (_log.IsErrorEnabled)
										_log.Error(sex);

									throw sex;
								}
							};
					}
					else
					{
						if (_log.IsDebugEnabled)
							_log.DebugFormat("SAGA: {0} Ignoring Existing {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
								typeof (TMessage).ToFriendlyName());
					}
				}
			}
			finally
			{
				if(needToLeave)
					Monitor.Exit(_sagas);
			}
		}

		public IEnumerable<Guid> Find(ISagaFilter<TSaga> filter)
		{
			return _sagas.Where(filter).Select(x => x.CorrelationId);
		}

		public IEnumerable<TSaga> Where(ISagaFilter<TSaga> filter)
		{
			return _sagas.Where(filter);
		}

		public IEnumerable<TResult> Where<TResult>(ISagaFilter<TSaga> filter, Func<TSaga, TResult> transformer)
		{
			return _sagas.Where(filter).Select(transformer);
		}

		public IEnumerable<TResult> Select<TResult>(Func<TSaga, TResult> transformer)
		{
			return _sagas.Select(transformer);
		}

		public void Add(TSaga newSaga)
		{
			lock (_sagas)
				_sagas.Add(newSaga);
		}

		public void Remove(TSaga saga)
		{
			lock (_sagas)
				_sagas.Remove(saga);
		}
	}
}