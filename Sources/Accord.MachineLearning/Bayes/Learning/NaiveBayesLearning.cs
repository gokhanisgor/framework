﻿// Accord Machine Learning Library
// The Accord.NET Framework
// http://accord-framework.net
//
// Copyright © César Souza, 2009-2017
// cesarsouza at gmail.com
//
//    This library is free software; you can redistribute it and/or
//    modify it under the terms of the GNU Lesser General Public
//    License as published by the Free Software Foundation; either
//    version 2.1 of the License, or (at your option) any later version.
//
//    This library is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//    Lesser General Public License for more details.
//
//    You should have received a copy of the GNU Lesser General Public
//    License along with this library; if not, write to the Free Software
//    Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
//

namespace Accord.MachineLearning.Bayes
{
    using Accord.Math;
    using Accord.Math.Optimization.Losses;
    using Accord.Statistics;
    using Accord.Statistics.Distributions;
    using Accord.Statistics.Distributions.Fitting;
    using Accord.Statistics.Distributions.Multivariate;
    using Accord.Statistics.Distributions.Univariate;
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Threading.Tasks;

#if !MONO
    /// <summary>
    ///   Naïve Bayes learning algorithm for discrete distribution models.
    /// </summary>
    /// 
    /// <example>
    /// <para>
    ///   For basic examples on how to learn a Naive Bayes algorithm, please see
    ///   <see cref="NaiveBayes"/> page. The following examples show how to set
    ///   more specialized learning settings for discrete models.</para>
    ///   
    /// <code source="Unit Tests\Accord.Tests.MachineLearning\Bayes\NaiveBayesTest.cs" region="doc_laplace" />
    /// </example>
    /// 
    /// <seealso cref="NaiveBayes"/>
    /// 
    [Serializable]
    public class NaiveBayesLearning :
        NaiveBayesLearningBase<NaiveBayes, GeneralDiscreteDistribution, int,
                           IndependentOptions<GeneralDiscreteOptions>,
                           GeneralDiscreteOptions>,
        ISupervisedLearning<NaiveBayes, int[], double[]>,
        ISupervisedLearning<NaiveBayes, int[], int>
    {

        /// <summary>
        /// Creates an instance of the model to be learned.
        /// </summary>
        protected override NaiveBayes Create(int[][] x, int y)
        {
            int[] inputs = x.DistinctCount();
            return new NaiveBayes(classes: y, symbols: inputs);
        }

        /// <summary>
        /// Learns a model that can map the given inputs to the given outputs.
        /// </summary>
        /// 
        /// <param name="x">The model inputs.</param>
        /// <param name="y">The desired outputs associated with each <paramref name="x">inputs</paramref>.</param>
        /// <param name="weight">The weight of importance for each input-output pair.</param>
        /// 
        /// <returns>
        ///   A model that has learned how to produce <paramref name="y" /> given <paramref name="x" />.
        /// </returns>
        /// 
        public override NaiveBayes Learn(int[][] x, int[] y, double[] weight = null)
        {
            CheckArgs(x, y);

            if (Model == null)
                Model = Create(x, y.DistinctCount());

            // For each class
            Parallel.For(0, Model.NumberOfOutputs, ParallelOptions, i =>
            {
                // Estimate conditional distributions
                // Get variables values in class i
                int[] idx = y.Find(y_i => y_i == i);
                int[][] values = x.Get(idx);

                int n = idx.Length;

                if (Empirical)
                    Model.Priors[i] = n / (double)x.Length;

                double regularization = Options.InnerOption.Regularization;
                if (Options.InnerOptions != null)
                    regularization = Options.InnerOptions[i].Regularization;

                // TODO: Remove Laplace rule. It does the same as regularization
                bool laplace = Options.InnerOption.UseLaplaceRule;
                if (Options.InnerOptions != null)
                    laplace = Options.InnerOptions[i].UseLaplaceRule;

                if (laplace)
                    regularization += 1;

                bool priors = Options.InnerOption.UsePreviousValuesAsPriors;
                if (Options.InnerOptions != null)
                    priors = Options.InnerOptions[i].UsePreviousValuesAsPriors;

                // For each variable (col)
                Parallel.For(0, Model.NumberOfInputs, ParallelOptions, j =>
                {
                    // Count value occurrences and store symbol frequencies 
                    var frequencies = new double[Model.NumberOfSymbols[j]];
                    for (int k = 0; k < values.Length; k++)
                        frequencies[values[k][j]]++;

                    // Transform into probabilities
                    probabilities(regularization, priors, frequencies, Model.Distributions[i, j]);
                });
            });

            return Model;
        }

        

        /// <summary>
        /// Learns a model that can map the given inputs to the given outputs.
        /// </summary>
        /// 
        /// <param name="x">The model inputs.</param>
        /// <param name="y">The desired outputs associated with each <paramref name="x">inputs</paramref>.</param>
        /// <param name="weight">The weight of importance for each input-output pair.</param>
        /// 
        /// <returns>
        ///   A model that has learned how to produce <paramref name="y" /> given <paramref name="x" />.
        /// </returns>
        /// 
        public override NaiveBayes Learn(int[][] x, double[][] y, double[] weight = null)
        {
            CheckArgs(x, y);

            if (Model == null)
                Model = Create(x, y[0].Length);

            if (Options.InnerOptions != null)
                for (int i = 0; i < Options.InnerOptions.Length; i++)
                    Options.InnerOptions[i] = Options.InnerOption;

            // For each class
            Parallel.For(0, Model.NumberOfOutputs, ParallelOptions, i =>
            {
                // Estimate conditional distributions
                // Get variables values in class i
                double sumOfWeights = 0;
                for (int j = 0; j < y.Length; j++)
                    sumOfWeights += y[j][i];

                if (Empirical)
                    Model.Priors[i] = sumOfWeights / x.Length;

                double regularization = Options.InnerOptions[i].Regularization;
                bool priors = Options.InnerOptions[i].UsePreviousValuesAsPriors;

                // TODO: Remove Laplace rule. It does the same as regularization
                if (Options.InnerOptions[i].UseLaplaceRule)
                    regularization += 1;

                // For each variable (col)
                Parallel.For(0, Model.NumberOfInputs, ParallelOptions, j =>
                {
                    // Count value occurrences and store symbol frequencies 
                    var frequencies = new double[Model.NumberOfSymbols[j]];
                    for (int k = 0; k < x.Length; k++)
                        frequencies[x[k][j]] += y[i][j];

                    // Transform into probabilities
                    probabilities(regularization, priors, frequencies, Model.Distributions[i, j]);
                });
            });

            return Model;
        }

        private static void probabilities(double regularization, bool priors, double[] frequencies, double[] probabilities)
        {
            double sum = 0;
            if (priors)
            {
                for (int k = 0; k < frequencies.Length; k++)
                    sum += probabilities[k] *= frequencies[k] + regularization;
            }
            else
            {
                for (int k = 0; k < frequencies.Length; k++)
                    sum += probabilities[k] = frequencies[k] + regularization;
            }

            probabilities.Divide(sum, result: probabilities);
        }
    }

#else
    /// <summary>
    ///   This class is currently not supported in Mono due to
    ///   a bug in the Mono compiler.
    /// </summary>
    /// 
    [Obsolete("This class is not supported in Mono due to a bug in the Mono compiler.")]
    public class NaiveBayesLearning
    {
    }
#endif
}
