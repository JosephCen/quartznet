#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */

#endregion

using System;
using System.Collections.Generic;
using System.Threading;
using Quartz.Spi;
using System.Runtime.Serialization;

namespace Quartz.Impl
{
    /// <summary>
    /// A context bundle containing handles to various environment information, that
    /// is given to a <see cref="JobDetail" /> instance as it is
    /// executed, and to a <see cref="ITrigger" /> instance after the
    /// execution completes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="JobDataMap" /> found on this object (via the 
    /// <see cref="MergedJobDataMap" /> method) serves as a convenience -
    /// it is a merge of the <see cref="JobDataMap" /> found on the 
    /// <see cref="JobDetail" /> and the one found on the <see cref="ITrigger" />, with 
    /// the value in the latter overriding any same-named values in the former.
    /// <i>It is thus considered a 'best practice' that the Execute code of a Job
    /// retrieve data from the JobDataMap found on this object</i> 
    /// </para>
    /// <para>
    /// NOTE: Do not
    /// expect value 'set' into this JobDataMap to somehow be set back onto a
    /// job's own JobDataMap.
    /// </para>
    /// 
    /// <para>
    /// <see cref="IJobExecutionContext" /> s are also returned from the 
    /// <see cref="IScheduler.GetCurrentlyExecutingJobs()" />
    /// method. These are the same instances as those past into the jobs that are
    /// currently executing within the scheduler. The exception to this is when your
    /// application is using Quartz remotely (i.e. via remoting or WCF) - in which case you get
    /// a clone of the <see cref="IJobExecutionContext" />s, and their references to
    /// the <see cref="IScheduler" /> and <see cref="IJob" /> instances have been lost (a
    /// clone of the <see cref="JobDetail" /> is still available - just not a handle
    /// to the job instance that is running).
    /// </para>
    /// </remarks>
    /// <seealso cref="JobDetail" /> 
    /// <seealso cref="IScheduler" />
    /// <seealso cref="IJob" />
    /// <seealso cref="ITrigger" />
    /// <seealso cref="JobDataMap" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    [DataContract]
    // TODO (NetCore Port) :    Figure out how these types are serialized and if Quartz always controls the serialization,
    //                          consider removing KnownType attributes from Quartz types and listing the types in the serializer.
    [KnownType(typeof(Triggers.AbstractTrigger))]
    [KnownType(typeof(Triggers.CalendarIntervalTriggerImpl))]
    [KnownType(typeof(Triggers.CronTriggerImpl))]
    [KnownType(typeof(Triggers.DailyTimeIntervalTriggerImpl))]
    [KnownType(typeof(Triggers.SimpleTriggerImpl))]
    [KnownType(typeof(JobDetailImpl))]
    [KnownType(typeof(Calendar.AnnualCalendar))]
    [KnownType(typeof(Calendar.BaseCalendar))]
    [KnownType(typeof(Calendar.CronCalendar))]
    [KnownType(typeof(Calendar.DailyCalendar))]
    [KnownType(typeof(Calendar.HolidayCalendar))]
    [KnownType(typeof(Calendar.MonthlyCalendar))]
    [KnownType(typeof(Calendar.WeeklyCalendar))]
    public class JobExecutionContextImpl : ICancellableJobExecutionContext
    {
#if BINARY_SERIALIZATION
        [NonSerialized]
#endif // BINARY_SERIALIZATION
        private readonly IScheduler scheduler;

#if BINARY_SERIALIZATION
        [NonSerialized]
#endif // BINARY_SERIALIZATION
        private readonly IJob job;

        [DataMember] private readonly ITrigger trigger;
        [DataMember] private readonly IJobDetail jobDetail;
        [DataMember] private readonly JobDataMap jobDataMap;

        [DataMember] private readonly ICalendar calendar;
        [DataMember] private readonly bool recovering;
        [DataMember] private int numRefires;
        [DataMember] private readonly DateTimeOffset? prevFireTimeUtc;
        [DataMember] private readonly DateTimeOffset? nextFireTimeUtc;
        [DataMember] private TimeSpan jobRunTime = TimeSpan.MinValue;
        [DataMember] private object result; // Note that DCS will limit what sorts of results can be serialized

        [DataMember] private readonly IDictionary<object, object> data = new Dictionary<object, object>();
        private readonly CancellationTokenSource cancellationTokenSource;

        /// <summary>
        /// Create a JobExecutionContext with the given context data.
        /// </summary>
        public JobExecutionContextImpl(IScheduler scheduler, TriggerFiredBundle firedBundle, IJob job)
        {
            this.scheduler = scheduler;
            trigger = firedBundle.Trigger;
            calendar = firedBundle.Calendar;
            jobDetail = firedBundle.JobDetail;
            this.job = job;
            recovering = firedBundle.Recovering;
            FireTimeUtc = firedBundle.FireTimeUtc;
            ScheduledFireTimeUtc = firedBundle.ScheduledFireTimeUtc;
            prevFireTimeUtc = firedBundle.PrevFireTimeUtc;
            nextFireTimeUtc = firedBundle.NextFireTimeUtc;

            jobDataMap = new JobDataMap();
            jobDataMap.PutAll(jobDetail.JobDataMap);
            jobDataMap.PutAll(trigger.JobDataMap);
            cancellationTokenSource = new CancellationTokenSource();
            CancellationToken = cancellationTokenSource.Token;
        }

        /// <summary>
        /// Get a handle to the <see cref="IScheduler" /> instance that fired the
        /// <see cref="IJob" />.
        /// </summary>
        public virtual IScheduler Scheduler => scheduler;

        /// <summary>
        /// Get a handle to the <see cref="ITrigger" /> instance that fired the
        /// <see cref="IJob" />.
        /// </summary>
        public virtual ITrigger Trigger => trigger;

        /// <summary>
        /// Get a handle to the <see cref="ICalendar" /> referenced by the <see cref="ITrigger" />
        /// instance that fired the <see cref="IJob" />.
        /// </summary>
        public virtual ICalendar Calendar => calendar;

        /// <summary>
        /// If the <see cref="IJob" /> is being re-executed because of a 'recovery'
        /// situation, this method will return <see langword="true" />.
        /// </summary>
        public virtual bool Recovering => recovering;

        public TriggerKey RecoveringTriggerKey
        {
            get
            {
                if (Recovering)
                {
                    return new TriggerKey(jobDataMap.GetString(SchedulerConstants.FailedJobOriginalTriggerName),
                        jobDataMap.GetString(SchedulerConstants.FailedJobOriginalTriggerGroup));
                }

                throw new InvalidOperationException("Not a recovering job");
            }
        }

        /// <summary>
        /// Gets the refire count.
        /// </summary>
        /// <value>The refire count.</value>
        public virtual int RefireCount => numRefires;

        /// <summary>
        /// Get the convenience <see cref="JobDataMap" /> of this execution context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <see cref="JobDataMap" /> found on this object serves as a convenience -
        /// it is a merge of the <see cref="JobDataMap" /> found on the 
        /// <see cref="JobDetail" /> and the one found on the <see cref="ITrigger" />, with 
        /// the value in the latter overriding any same-named values in the former.
        /// <i>It is thus considered a 'best practice' that the Execute code of a Job
        /// retrieve data from the JobDataMap found on this object.</i>
        /// </para>
        /// <para>
        /// NOTE: Do not expect value 'set' into this JobDataMap to somehow be 
        /// set back onto a job's own JobDataMap.
        /// </para>
        /// <para>
        /// Attempts to change the contents of this map typically result in an 
        /// illegal state.
        /// </para>
        /// </remarks>
        public virtual JobDataMap MergedJobDataMap => jobDataMap;

        /// <summary>
        /// Get the <see cref="JobDetail" /> associated with the <see cref="IJob" />.
        /// </summary>
        public virtual IJobDetail JobDetail => jobDetail;

        /// <summary>
        /// Get the instance of the <see cref="IJob" /> that was created for this
        /// execution.
        /// <para>
        /// Note: The Job instance is not available through remote scheduler
        /// interfaces.
        /// </para>
        /// </summary>
        public virtual IJob JobInstance => job;

        /// <summary>
        /// The actual time the trigger fired. For instance the scheduled time may
        /// have been 10:00:00 but the actual fire time may have been 10:00:03 if
        /// the scheduler was too busy.
        /// </summary>
        /// <returns> Returns the fireTimeUtc.</returns>
        /// <seealso cref="ScheduledFireTimeUtc" />
        public DateTimeOffset? FireTimeUtc { get; }

        /// <summary> 
        /// The scheduled time the trigger fired for. For instance the scheduled
        /// time may have been 10:00:00 but the actual fire time may have been
        /// 10:00:03 if the scheduler was too busy.
        /// </summary>
        /// <returns> Returns the scheduledFireTimeUtc.</returns>
        /// <seealso cref="FireTimeUtc" />
        public DateTimeOffset? ScheduledFireTimeUtc { get; }

        /// <summary>
        /// Gets the previous fire time.
        /// </summary>
        /// <value>The previous fire time.</value>
        public DateTimeOffset? PreviousFireTimeUtc => prevFireTimeUtc;

        /// <summary>
        /// Gets the next fire time.
        /// </summary>
        /// <value>The next fire time.</value>
        public DateTimeOffset? NextFireTimeUtc => nextFireTimeUtc;

        /// <summary>
        /// Returns the result (if any) that the <see cref="IJob" /> set before its 
        /// execution completed (the type of object set as the result is entirely up 
        /// to the particular job).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The result itself is meaningless to Quartz, but may be informative
        /// to <see cref="IJobListener" />s or 
        /// <see cref="ITriggerListener" />s that are watching the job's 
        /// execution.
        /// </para> 
        /// 
        /// Set the result (if any) of the <see cref="IJob" />'s execution (the type of 
        /// object set as the result is entirely up to the particular job).
        /// 
        /// <para>
        /// The result itself is meaningless to Quartz, but may be informative
        /// to <see cref="IJobListener" />s or 
        /// <see cref="ITriggerListener" />s that are watching the job's 
        /// execution.
        /// </para> 
        /// </remarks>
        public virtual object Result
        {
            get { return result; }
            set { result = value; }
        }

        /// <summary> 
        /// The amount of time the job ran for.  The returned 
        /// value will be <see cref="TimeSpan.MinValue" /> until the job has actually completed (or thrown an 
        /// exception), and is therefore generally only useful to 
        /// <see cref="IJobListener" />s and <see cref="ITriggerListener" />s.
        /// </summary>
        public virtual TimeSpan JobRunTime
        {
            get
            {
                if (jobRunTime == TimeSpan.MinValue && FireTimeUtc != null)
                {
                    // we are still in progress, calculate dynamically
                    return DateTimeOffset.UtcNow - FireTimeUtc.Value;
                }

                return jobRunTime;
            }
            set { jobRunTime = value; }
        }

        /// <summary>
        /// Increments the refire count.
        /// </summary>
        public virtual void IncrementRefireCount()
        {
            Interlocked.Increment(ref numRefires);
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return
                $"JobExecutionContext: trigger: '{Trigger.Key}' job: '{JobDetail.Key}' fireTimeUtc: '{FireTimeUtc:r}' scheduledFireTimeUtc: '{ScheduledFireTimeUtc:r}' previousFireTimeUtc: '{PreviousFireTimeUtc:r}' nextFireTimeUtc: '{NextFireTimeUtc:r}' recovering: {Recovering} refireCount: {RefireCount}";
        }

        /// <summary> 
        /// Put the specified value into the context's data map with the given key.
        /// Possibly useful for sharing data between listeners and jobs.
        /// <para>
        /// NOTE: this data is volatile - it is lost after the job execution
        /// completes, and all TriggerListeners and JobListeners have been 
        /// notified.
        /// </para> 
        /// </summary>
        /// <param name="key">
        /// </param>
        /// <param name="objectValue">
        /// </param>
        public virtual void Put(object key, object objectValue)
        {
            data[key] = objectValue;
        }

        /// <summary> 
        /// Get the value with the given key from the context's data map.
        /// </summary>
        /// <param name="key">
        /// </param>
        public virtual object Get(object key)
        {
            object retValue;
            data.TryGetValue(key, out retValue);
            return retValue;
        }

        public virtual void Cancel()
        {
            cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Returns the fire instance id.
        /// </summary>
        public string FireInstanceId => ((IOperableTrigger) trigger).FireInstanceId;

        public CancellationToken CancellationToken { get; }
    }
}