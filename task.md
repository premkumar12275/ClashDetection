* Backend Service: Building Clashes Detection *

**Objective:**
Create a backend service with a HTTP API that can detect clashes between buildings in a 3D space. The service should accept a list of buildings as input and produce the overlapping sections as output.

**Input Data:**
The input data will be provided in the form of geojson FeatureCollection with Polygon features representing buildings. Each building will have the properties `id`, `height` and `elevation`, specified in meters. Refer to the `input-sample1.json` file for an example.

**Output Data:**
The output data should be a geojson FeatureCollection containing Polygon features that represent the overlapping sections between buildings. The overlap features should have the properties `height`, `elevation` and `buildingIds`. The `buildingIds` should correspond to the buildings that overlapps in each section. If no overlaps are found, the response should be an empty FeatureCollection. See `output-sample1.json` as an example output for `input-sample1.json`.

***Note:*** If more than two buildings overlap in the same section you can choose to either report a single overlap or to return each pair-wise overlap. (see `input-sample2.json` and `output-sample2.json` as an example of the pair-wise)

**Requirements:**
- The API should validate the input data and provide descriptive error messages for invalid inputs.
- Typical API clients will close their connection (time out) after some seconds, we will use 10 seconds as an example here. Your design should handle computations that last longer than 10 seconds.
- The service should implement caching for subsequent requests with the same input data.
- the application should be scalable to production deployment.

**Additional**
- Build a .Net minimal API using latest framework.
- You can use any library or framework to assist in solving the task.

